using System.ComponentModel;
using D365Architect.Commands.Choice;
using D365Architect.Commands.Form;
using D365Architect.Commands.Table;
using D365Architect.Commands.View;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Solution;

/// <summary>
/// `d365architect solution import --input ./examplesolution --solution examplesolution [--table account] [--yes] [--whatif] [--verbose] [--no-transaction] [--allow-schema-violations] [--no-publish]`
/// Walks a folder previously written by <c>solution export</c> — a
/// <c>choices.yml</c> at its root, plus one sub-folder per table holding
/// that table's own <c>*.table.yml</c>/<c>*.view.yml</c>/<c>*.form.yml</c>
/// files — and imports every file found, in two stages (the order itself
/// lives in <see cref="SolutionImportOrchestrator"/>): first every
/// <c>*.table.yml</c> is previewed as `table create` would, the tables that
/// don't exist live yet are listed, and after one "Create these N tables?"
/// they're all created (primary name column only) in one Dataverse
/// transaction — see <see cref="StepRunner.CreateTablesAsync"/>. Then
/// everything is previewed in the same order `solution export` would have
/// written it (choices first, then each table folder's own table, then its
/// views, then its forms). Each of those previews reads live state afresh,
/// so it sees the tables just created.
///
/// The diffs of the files that changed are printed, then it asks once
/// ("Import N changes?") and applies them all, in that same order; see
/// <see cref="SolutionImportFlow"/>. Unchanged files print nothing unless
/// <c>--verbose</c>. The run ends with "N changed, M unchanged, K failed".
/// So a run that creates tables asks twice: once to create them (their
/// columns, views and forms can't be previewed until they exist), once for
/// everything else.
///
/// Deliberately doesn't invent a new bulk-import mechanism: each file goes
/// through the same preview as `choice import`/`table import`/`view
/// import`/`form import` (<c>PreviewAsync</c>, the first half of each
/// command's <c>RunAsync</c>), so every asset still gets its own diff,
/// column/choice plan and safety gate (e.g. the form schema-violation
/// gate); only the prompt is shared. Applying a change publishes it as its
/// own import command would, unless <c>--no-publish</c>. <c>--yes</c> skips
/// both prompts; <c>--whatif</c> prints the diffs and never prompts or
/// writes. <c>--solution</c> is required (not optional the way it is on the
/// individual `table create`/`table import`/`choice import` commands) and is
/// forwarded to every table create/import, choice import, view import and
/// form import, so any brand-new table/column/lookup relationship/global
/// choice this run creates, and any view or form it creates or changes,
/// actually joins the solution it came from — see
/// <see cref="Services.Dataverse.IDataverseClient.CreateAttributeAsync"/>.
///
/// <c>--table</c> (repeatable) limits the run to those tables' files
/// (folder names matched case-insensitively; a name with no folder is an
/// error) and skips <c>choices.yml</c>.
///
/// A table that can't be created stops the whole run: the transaction is
/// rolled back so none is created, no later stage runs, and the table
/// Dataverse reported is named — see <see cref="SolutionImportOrchestrator"/>
/// for why. Declining the create prompt stops the run the same way. Under
/// <c>--whatif</c> a table that would be created is reported as such, and
/// its own table import, views and forms are skipped (there's nothing live
/// to preview them against) rather than counted as failures. Past the
/// create stage, one file failing (a validation error, a Dataverse
/// rejection, a missing live counterpart) doesn't stop the rest: it's
/// reported, never applied, and the remaining changes are still offered and
/// applied. The exit code is non-zero if anything failed. There's no
/// cross-file rollback.
/// </summary>
public sealed class ImportSolutionCommand(
    ITableCreateService tableCreateService,
    ITableImportService tableImportService,
    IViewImportService viewImportService,
    IFormImportService formImportService,
    IGlobalChoiceImportService globalChoiceImportService,
    ImportRunner importRunner) : AsyncCommand<ImportSolutionCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <DIRECTORY>")]
        [Description("Path to a solution folder previously written by 'solution export' (a choices.yml at its root, plus one sub-folder per table).")]
        public required string Input { get; init; }

        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of the solution these files came from. Forwarded to every table/choice/view/form import so any brand-new column/lookup relationship/global choice this run creates, and any view or form it creates or changes, actually joins the solution, rather than silently landing outside it.")]
        public required string Solution { get; init; }

        [CommandOption("-t|--table <LOGICAL_NAME>")]
        [Description("Limit the import to this table's table/view/form files (folder name, case-insensitive). Repeat for several. Global choices are skipped. Errors if a name matches no table folder.")]
        public string[] Tables { get; init; } = [];

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompts and create/import every changed asset immediately.")]
        public bool Yes { get; init; }

        [CommandOption("--whatif")]
        [Description("Only show each changed asset's diff/plan — never prompt and never write anything to Dataverse.")]
        public bool WhatIf { get; init; }

        [CommandOption("--verbose")]
        [Description("Also print a 'No changes' line for every asset that already matches what's live. Unchanged assets are silent by default.")]
        public bool Verbose { get; init; }

        [CommandOption("--no-transaction")]
        [Description("For every table import: send column create/update one request at a time instead of as a single atomic Dataverse changeset.")]
        public bool NoTransaction { get; init; }

        [CommandOption("--allow-schema-violations")]
        [Description("For every form import: proceed even if the rebuilt FormXML has a schema violation Dataverse might reject outright.")]
        public bool AllowSchemaViolations { get; init; }

        [CommandOption("--no-publish")]
        [Description("For every table/choice/view/form import: don't publish after importing.")]
        public bool NoPublish { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    /// <summary>The command's own logic, independent of Spectre's <see cref="CommandContext"/>. Internal so tests can run it end to end.</summary>
    internal async Task<int> RunAsync(Settings settings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(settings.Input))
        {
            ErrorConsole.Print($"'{settings.Input}' doesn't exist.");
            return 1;
        }

        var limitedToTables = settings.Tables.Length > 0;
        IReadOnlyList<string>? onlyTables = null;
        if (limitedToTables)
        {
            try
            {
                onlyTables = SolutionTableFilter.Select(Directory.GetDirectories(settings.Input).Select(Path.GetFileName)!, settings.Tables, $"under '{settings.Input}'");
            }
            catch (UnknownTableException ex)
            {
                ErrorConsole.Print($"{ex.Message}");
                return 1;
            }
        }

        var runner = new StepRunner(this, settings);
        var result = await new SolutionImportOrchestrator().RunAsync(settings.Input, runner, onlyTables, cancellationToken);

        if (result.StopWasDeclined)
        {
            ErrorConsole.Print($"Stopped: creating the tables was declined, so nothing was imported.");
            return 1;
        }

        if (result.StoppedAtAssetPath is { } stoppedAt)
        {
            ErrorConsole.Print($"Stopped: creating the tables failed at '{stoppedAt}', so none was created and no choices, columns, views or forms were imported.");
            return 1;
        }

        if (result.Succeeded == 0 && result.Failed == 0 && result.Skipped == 0)
        {
            if (limitedToTables)
            {
                ErrorConsole.Warn($"The selected table folder(s) under '{settings.Input}' contain no *.table.yml, *.view.yml or *.form.yml files — nothing to import.");
            }
            else
            {
                ErrorConsole.Warn($"No choices.yml, or table sub-folders containing *.table.yml/*.view.yml/*.form.yml files, found under '{settings.Input}' — nothing to import.");
            }

            return 0;
        }

        return await runner.ConfirmAndApplyAsync(cancellationToken);
    }

    /// <summary>
    /// Previews one import stage for one file through that asset's own command, with
    /// this command's options forwarded to every one they apply to. Kept as
    /// the single place a stage maps to a command so the orchestrator (the
    /// ordering) and the per-asset commands (the safety) stay separate.
    /// </summary>
    private Task<PreparedImport> PreviewStepAsync(Settings settings, SolutionImportStage stage, string assetPath, bool printUnchanged, IReadOnlySet<string> pendingChoiceNames, CancellationToken cancellationToken) => stage switch
    {
        SolutionImportStage.ImportChoices => new ImportChoiceCommand(globalChoiceImportService, importRunner).PreviewAsync(
            new ImportChoiceCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, NoPublish = settings.NoPublish, Solution = settings.Solution }, printUnchanged, cancellationToken),
        SolutionImportStage.ImportTable => new ImportTableCommand(tableImportService, importRunner).PreviewAsync(
            new ImportTableCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, NoPublish = settings.NoPublish, NoTransaction = settings.NoTransaction, Solution = settings.Solution }, printUnchanged, pendingChoiceNames, cancellationToken),
        SolutionImportStage.ImportView => new ImportViewCommand(viewImportService, importRunner).PreviewAsync(
            new ImportViewCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, NoPublish = settings.NoPublish, Solution = settings.Solution }, printUnchanged, cancellationToken),
        SolutionImportStage.ImportForm => new ImportFormCommand(formImportService, importRunner).PreviewAsync(
            new ImportFormCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, NoPublish = settings.NoPublish, Solution = settings.Solution, AllowSchemaViolations = settings.AllowSchemaViolations }, printUnchanged, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>
    /// The create stage (see <see cref="StepRunner.CreateTablesAsync"/>): previews every table, refusing the whole stage - nothing sent - if
    /// any file can't be read or used; lists the missing ones; asks once (unless <c>--yes</c>; under <c>--whatif</c> it only lists them);
    /// then creates them all in one transaction, where one failure keeps none.
    /// </summary>
    private async Task<SolutionTableCreateResult> CreateTablesAsync(Settings settings, IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken)
    {
        AuthenticatedContext auth;
        try
        {
            auth = await importRunner.GetContextAsync(cancellationToken);
        }
        catch (AuthenticationRequiredException ex)
        {
            ErrorConsole.Print(ex);
            return new SolutionTableCreateResult(SolutionImportStepOutcome.Failed, []);
        }

        var previews = new List<(string Path, TableCreatePreview Preview)>();
        foreach (var path in tableAssetPaths)
        {
            var entity = await YamlFileReader.TryReadAsync(path, "table", EntityYamlDeserializer.FromYaml, cancellationToken);
            if (entity is null)
            {
                return Failed(path);
            }

            try
            {
                var preview = await AnsiConsole.Status().StartAsync($"Checking whether '{entity.LogicalName}' exists...",
                    async _ => await tableCreateService.PreviewAsync(auth.EnvironmentUrl, auth.AccessToken, entity, cancellationToken));
                previews.Add((path, preview));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorConsole.Print($"'{path}' can't be created: {ex.Message}");
                return Failed(path);
            }
        }

        var toCreate = previews.Where(p => !p.Preview.AlreadyExists).ToList();
        if (toCreate.Count == 0)
        {
            if (settings.Verbose)
            {
                AnsiConsole.MarkupLine("[green]No tables to create[/] — every table already exists.");
                AnsiConsole.WriteLine();
            }

            return new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, []);
        }

        AnsiConsole.MarkupLine("[bold]Tables to create:[/]");
        foreach (var (_, preview) in toCreate)
        {
            AnsiConsole.MarkupLine($"[green]  + table {preview.EntityLogicalName.EscapeMarkup()} (create, with its primary name column {preview.PrimaryNameColumn?.EscapeMarkup()})[/]");
        }

        AnsiConsole.WriteLine();

        if (settings.WhatIf)
        {
            foreach (var (_, preview) in toCreate)
            {
                AnsiConsole.MarkupLine($"[grey]--whatif: '{preview.EntityLogicalName.EscapeMarkup()}' would be created; its columns, views and forms are skipped below.[/]");
            }

            AnsiConsole.WriteLine();
            return new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, toCreate.Select(p => p.Path).ToList());
        }

        var question = toCreate.Count == 1 ? "Create this table?" : $"Create these {toCreate.Count} tables?";
        if (!settings.Yes && !AnsiConsole.Confirm(question, defaultValue: false))
        {
            AnsiConsole.MarkupLine("[yellow]Aborted.[/] No table was created.");
            return new SolutionTableCreateResult(SolutionImportStepOutcome.Declined, []);
        }

        try
        {
            await AnsiConsole.Status().StartAsync($"Creating {toCreate.Count} table(s) in one transaction...",
                async _ => await tableCreateService.ApplyAllAsync(auth.EnvironmentUrl, auth.AccessToken, toCreate.Select(p => p.Preview).ToList(), settings.Solution, cancellationToken));
        }
        catch (TableCreateFailedException ex)
        {
            ErrorConsole.Print($"{ex.Message} No table was created.");
            return Failed(toCreate.First(p => p.Preview.EntityLogicalName == ex.EntityLogicalName).Path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorConsole.Print($"Creating the tables failed: {ex.Message} No table was created.");
            return new SolutionTableCreateResult(SolutionImportStepOutcome.Failed, []);
        }

        AnsiConsole.MarkupLine($"[green]Created.[/] {string.Join(", ", toCreate.Select(p => p.Preview.EntityLogicalName)).EscapeMarkup()}.");
        AnsiConsole.WriteLine();
        return new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, []);

        static SolutionTableCreateResult Failed(string path) => new(SolutionImportStepOutcome.Failed, [], path);
    }

    /// <summary>
    /// The production <see cref="ISolutionImportStepRunner"/>. <see cref="RunAsync"/> only previews (via
    /// <see cref="PreviewStepAsync"/> and <see cref="SolutionImportFlow.PreviewAsync"/>), holding every change;
    /// <see cref="ConfirmAndApplyAsync"/>, called once the orchestrator has finished, asks once and applies them.
    /// Internal so tests can check the wiring.
    /// </summary>
    internal sealed class StepRunner : ISolutionImportStepRunner
    {
        private readonly ImportSolutionCommand _command;
        private readonly Settings _settings;
        private readonly SolutionImportFlow _flow;
        private readonly IReadOnlySet<string> _pendingChoiceNames;

        public StepRunner(ImportSolutionCommand command, Settings settings)
        {
            _command = command;
            _settings = settings;
            _flow = new SolutionImportFlow(settings.Verbose);
            _pendingChoiceNames = ReadPendingChoiceNames(settings);
        }

        public async Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            var item = new SolutionImportItem(assetPath, (printUnchanged, ct) =>
                _command.PreviewStepAsync(_settings, stage, assetPath, printUnchanged, _pendingChoiceNames, ct));
            return await _flow.PreviewAsync(item, cancellationToken) == ImportOutcome.Failed
                ? SolutionImportStepOutcome.Failed
                : SolutionImportStepOutcome.Succeeded;
        }

        /// <summary>Asks once (unless <c>--yes</c>; never under <c>--whatif</c>) and applies every change <see cref="RunAsync"/> previewed. Returns the exit code.</summary>
        public Task<int> ConfirmAndApplyAsync(CancellationToken cancellationToken) =>
            _flow.ConfirmAndApplyAsync(_settings.Yes, _settings.WhatIf, confirm: null, cancellationToken);

        public Task<SolutionTableCreateResult> CreateTablesAsync(IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken) =>
            _command.CreateTablesAsync(_settings, tableAssetPaths, cancellationToken);

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName)
        {
            AnsiConsole.MarkupLine($"[grey]'{assetPath.EscapeMarkup()}' skipped: '{tableLogicalName.EscapeMarkup()}' doesn't exist yet and would be created first.[/]");
            _flow.CountSkipped();
        }

        /// <summary>
        /// Previews all run before anything is applied, so a column bound to a
        /// global choice that choices.yml creates in this same run can't see
        /// that choice live yet. Every table preview gets the names in
        /// choices.yml so it plans the column as a create; the apply order
        /// (choices first) makes that true by the time the table is written.
        /// Empty under <c>--table</c>, which skips choices.yml.
        /// </summary>
        private static IReadOnlySet<string> ReadPendingChoiceNames(Settings settings)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var choicesPath = Path.Combine(settings.Input, "choices.yml");
            if (settings.Tables.Length > 0 || !File.Exists(choicesPath))
            {
                return names;
            }

            try
            {
                foreach (var choice in GlobalChoiceYamlDeserializer.FromYaml(File.ReadAllText(choicesPath)))
                {
                    names.Add(choice.Name);
                }
            }
            catch (YamlDotNet.Core.YamlException)
            {
                // The choices step reports the unparseable file itself.
            }

            return names;
        }
    }
}

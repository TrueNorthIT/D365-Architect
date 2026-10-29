using System.ComponentModel;
using D365Architect.Commands.Choice;
using D365Architect.Commands.Form;
using D365Architect.Commands.Table;
using D365Architect.Commands.View;
using D365Architect.Services.Conversion;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Solution;

/// <summary>
/// `d365architect solution import --input ./examplesolution --solution examplesolution [--yes] [--whatif] [--no-transaction] [--allow-schema-violations]`
/// Walks a folder previously written by <c>solution export</c> — a
/// <c>choices.yml</c> at its root, plus one sub-folder per table holding
/// that table's own <c>*.table.yml</c>/<c>*.view.yml</c>/<c>*.form.yml</c>
/// files — and imports every file found, in two stages (the order itself
/// lives in <see cref="SolutionImportOrchestrator"/>): first every
/// <c>*.table.yml</c> goes through `table create`, so a table that doesn't
/// exist live yet is created (primary name column only; one that already
/// exists is just reported), then everything is imported in the same order
/// `solution export` would have written it (choices first, then each table
/// folder's own table, then its views, then its forms). Each of those
/// previews reads live state afresh, so it sees the tables just created.
///
/// Deliberately doesn't invent a new bulk-import mechanism: each file is run
/// through the exact same <c>RunAsync</c> entry point `choice import`/`table
/// import`/`view import`/`form import` already expose (pulled out of each
/// command's own <c>ExecuteAsync</c> specifically for this), so every asset
/// still gets its own preview → diff → confirm → apply flow, its own column/
/// choice plan, and its own schema-violation gate — nothing here bypasses
/// any of that per-asset safety. <c>--yes</c>/<c>--whatif</c>/
/// <c>--no-transaction</c>/<c>--allow-schema-violations</c> are forwarded to
/// every file they apply to, so `--yes` runs the whole solution unattended
/// the same way it does for a single asset today. <c>--solution</c> is
/// required (not optional the way it is on the individual `table create`/
/// `table import`/`choice import` commands) and is likewise forwarded to
/// every table create/import and choice import, so any brand-new table/
/// column/lookup relationship/global choice
/// this run creates actually joins the solution it came from — see
/// <see cref="Services.Dataverse.IDataverseClient.CreateAttributeAsync"/>'s
/// own doc comment for the gap this closes; without it, a new asset would
/// exist live but silently not show up in a later `solution export` of the
/// same solution.
///
/// A table that can't be created stops the whole run there: no further
/// creates and no later stages, with the table named — see
/// <see cref="SolutionImportOrchestrator"/> for why. Past the create stage,
/// one file failing (a validation error, a Dataverse rejection, a missing
/// live counterpart) doesn't stop the rest — this keeps going through every
/// remaining file so one bad form doesn't block importing everything else,
/// then reports how many succeeded/failed at the end and exits non-zero if
/// anything did. There's no cross-file rollback: each asset's own write is
/// already atomic on its own terms (`table import`'s changeset, in
/// particular), but nothing ties multiple files' writes together into one
/// larger transaction.
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
        [Description("Unique name of the solution these files came from. Forwarded to every table/choice import so any brand-new column/lookup relationship/global choice this run creates actually joins the solution, rather than silently landing outside it.")]
        public required string Solution { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt for every asset and import immediately.")]
        public bool Yes { get; init; }

        [CommandOption("--whatif")]
        [Description("Only show each asset's diff/plan — never prompt and never write anything to Dataverse.")]
        public bool WhatIf { get; init; }

        [CommandOption("--no-transaction")]
        [Description("For every table import: send column create/update one request at a time instead of as a single atomic Dataverse changeset.")]
        public bool NoTransaction { get; init; }

        [CommandOption("--allow-schema-violations")]
        [Description("For every form import: proceed even if the rebuilt FormXML has a schema violation Dataverse might reject outright.")]
        public bool AllowSchemaViolations { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(settings.Input))
        {
            ErrorConsole.Print($"'{settings.Input}' doesn't exist.");
            return 1;
        }

        var result = await new SolutionImportOrchestrator().RunAsync(settings.Input, new StepRunner(this, settings), cancellationToken);

        if (result.StoppedAtAssetPath is { } stoppedAt)
        {
            ErrorConsole.Print($"Stopped: the table in '{stoppedAt}' couldn't be created, so no later tables were created and no choices, columns, views or forms were imported. {result.Succeeded} succeeded before it.");
            return 1;
        }

        if (result.Succeeded == 0 && result.Failed == 0)
        {
            ErrorConsole.Warn($"No choices.yml or table sub-folders found under '{settings.Input}' — nothing to import.");
            return 0;
        }

        AnsiConsole.MarkupLine(result.Failed == 0
            ? $"[green]Done.[/] {result.Succeeded} asset(s) processed."
            : $"[yellow]Done with errors.[/] {result.Succeeded} succeeded, {result.Failed} failed — see above.");

        return result.Failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Runs one stage for one file through that asset's own command, with
    /// this command's options forwarded to every one they apply to. Kept as
    /// the single place a stage maps to a command so the orchestrator (the
    /// ordering) and the per-asset commands (the safety) stay separate.
    /// </summary>
    private Task<int> RunStepAsync(Settings settings, SolutionImportStage stage, string assetPath, CancellationToken cancellationToken) => stage switch
    {
        SolutionImportStage.CreateTable => new CreateTableCommand(tableCreateService, importRunner).RunAsync(
            new CreateTableCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, Solution = settings.Solution }, cancellationToken),
        SolutionImportStage.ImportChoices => new ImportChoiceCommand(globalChoiceImportService, importRunner).RunAsync(
            new ImportChoiceCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, Solution = settings.Solution }, cancellationToken),
        SolutionImportStage.ImportTable => new ImportTableCommand(tableImportService, importRunner).RunAsync(
            new ImportTableCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, NoTransaction = settings.NoTransaction, Solution = settings.Solution }, cancellationToken),
        SolutionImportStage.ImportView => new ImportViewCommand(viewImportService, importRunner).RunAsync(
            new ImportViewCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf }, cancellationToken),
        SolutionImportStage.ImportForm => new ImportFormCommand(formImportService, importRunner).RunAsync(
            new ImportFormCommand.Settings { Input = assetPath, Yes = settings.Yes, WhatIf = settings.WhatIf, AllowSchemaViolations = settings.AllowSchemaViolations }, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>The production <see cref="ISolutionImportStepRunner"/>: <see cref="RunStepAsync"/> behind <see cref="TryRunAsync"/>, plus the blank line between assets this command always printed. Internal so tests can check the wiring.</summary>
    internal sealed class StepRunner(ImportSolutionCommand command, Settings settings) : ISolutionImportStepRunner
    {
        public async Task<bool> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            var succeeded = await TryRunAsync(assetPath, () => command.RunStepAsync(settings, stage, assetPath, cancellationToken));
            AnsiConsole.WriteLine();
            return succeeded;
        }
    }

    /// <summary>
    /// Runs one file's import, catching anything it throws so one bad file
    /// genuinely can't stop the rest — the resilience this class's own doc
    /// comment already claimed, but that nothing here actually enforced
    /// until now: <see cref="ImportRunner.RunAsync{TSettings,TInput,TPreview}"/>
    /// only ever translates a small, per-command allowlist of exception
    /// types into a clean exit code (e.g. table/choice import only recognize
    /// <see cref="InvalidDataException"/>) — anything else used to propagate
    /// straight out of this whole command, aborting every remaining file.
    /// </summary>
    private static async Task<bool> TryRunAsync(string assetPath, Func<Task<int>> run)
    {
        try
        {
            return await run() == 0;
        }
        catch (Exception ex)
        {
            ErrorConsole.Print($"'{assetPath}' failed: {ex.Message}");
            return false;
        }
    }
}

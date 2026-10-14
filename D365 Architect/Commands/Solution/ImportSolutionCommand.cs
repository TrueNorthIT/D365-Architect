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
/// `d365architect solution import --input ./examplesolution --solution examplesolution [--yes] [--whatif] [--verbose] [--no-transaction] [--allow-schema-violations]`
/// Walks a folder previously written by <c>solution export</c> — a
/// <c>choices.yml</c> at its root, plus one sub-folder per table holding
/// that table's own <c>*.table.yml</c>/<c>*.view.yml</c>/<c>*.form.yml</c>
/// files — in the same order `solution export` would have written them
/// (choices first, then each table folder's own table, then its views, then
/// its forms).
///
/// It previews every file first, prints the diffs of the ones that changed,
/// then asks once ("Import N changes?") and applies them all; see
/// <see cref="SolutionImportFlow"/>. Unchanged files print nothing unless
/// <c>--verbose</c>. The run ends with "N changed, M unchanged, K failed".
///
/// Deliberately doesn't invent a new bulk-import mechanism: each file goes
/// through the same preview as `choice import`/`table import`/`view
/// import`/`form import` (<c>PreviewAsync</c>, the first half of each
/// command's <c>RunAsync</c>), so every asset still gets its own diff,
/// column/choice plan and safety gate (e.g. the form schema-violation
/// gate); only the prompt is shared. <c>--yes</c> skips it; <c>--whatif</c>
/// prints the diffs and never prompts or writes. <c>--solution</c> is
/// required (not optional the way it is on the individual `table import`/
/// `choice import` commands) and is forwarded to every table/choice
/// import, so any brand-new column/lookup relationship/global choice this
/// run creates actually joins the solution it came from — see
/// <see cref="Services.Dataverse.IDataverseClient.CreateAttributeAsync"/>.
///
/// One file failing (a validation error, a Dataverse rejection, a missing
/// live counterpart) doesn't stop the rest: it's reported, never applied,
/// and the remaining changes are still offered and applied. The exit code
/// is non-zero if anything failed. There's no cross-file rollback.
/// </summary>
public sealed class ImportSolutionCommand(
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
        [Description("Skip the confirmation prompt and import every changed asset immediately.")]
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
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(settings.Input))
        {
            ErrorConsole.Print($"'{settings.Input}' doesn't exist.");
            return 1;
        }

        var tableDirectories = Directory.GetDirectories(settings.Input).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

        var items = new List<SolutionImportItem>();

        // Previews all run before anything is applied, so a column bound to a
        // global choice that choices.yml creates in this same run can't see
        // that choice live yet. Hand every table preview the names in
        // choices.yml so it plans the column as a create; the apply order
        // (choices first) makes that true by the time the table is written.
        var pendingChoiceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var choicesPath = Path.Combine(settings.Input, "choices.yml");
        if (File.Exists(choicesPath))
        {
            try
            {
                foreach (var choice in GlobalChoiceYamlDeserializer.FromYaml(File.ReadAllText(choicesPath)))
                {
                    pendingChoiceNames.Add(choice.Name);
                }
            }
            catch (YamlDotNet.Core.YamlException)
            {
                // The choice item below reports the unparseable file itself.
            }

            var choiceSettings = new ImportChoiceCommand.Settings { Input = choicesPath, Yes = settings.Yes, WhatIf = settings.WhatIf, Solution = settings.Solution };
            items.Add(new SolutionImportItem(choicesPath, (printUnchanged, ct) =>
                new ImportChoiceCommand(globalChoiceImportService, importRunner).PreviewAsync(choiceSettings, printUnchanged, ct)));
        }

        foreach (var entityDirectory in tableDirectories)
        {
            foreach (var tablePath in Directory.GetFiles(entityDirectory, "*.table.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var tableSettings = new ImportTableCommand.Settings { Input = tablePath, Yes = settings.Yes, WhatIf = settings.WhatIf, NoTransaction = settings.NoTransaction, Solution = settings.Solution };
                items.Add(new SolutionImportItem(tablePath, (printUnchanged, ct) =>
                    new ImportTableCommand(tableImportService, importRunner).PreviewAsync(tableSettings, printUnchanged, pendingChoiceNames, ct)));
            }

            foreach (var viewPath in Directory.GetFiles(entityDirectory, "*.view.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var viewSettings = new ImportViewCommand.Settings { Input = viewPath, Yes = settings.Yes, WhatIf = settings.WhatIf };
                items.Add(new SolutionImportItem(viewPath, (printUnchanged, ct) =>
                    new ImportViewCommand(viewImportService, importRunner).PreviewAsync(viewSettings, printUnchanged, ct)));
            }

            foreach (var formPath in Directory.GetFiles(entityDirectory, "*.form.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var formSettings = new ImportFormCommand.Settings { Input = formPath, Yes = settings.Yes, WhatIf = settings.WhatIf, AllowSchemaViolations = settings.AllowSchemaViolations };
                items.Add(new SolutionImportItem(formPath, (printUnchanged, ct) =>
                    new ImportFormCommand(formImportService, importRunner).PreviewAsync(formSettings, printUnchanged, ct)));
            }
        }

        if (items.Count == 0)
        {
            ErrorConsole.Warn($"No choices.yml, or table sub-folders containing *.table.yml/*.view.yml/*.form.yml files, found under '{settings.Input}' — nothing to import.");

            return 0;
        }

        return await SolutionImportFlow.RunAsync(items, settings.Yes, settings.WhatIf, settings.Verbose, confirm: null, cancellationToken);
    }
}

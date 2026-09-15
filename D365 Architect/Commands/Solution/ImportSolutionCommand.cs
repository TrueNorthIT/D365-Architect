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
/// files — and imports every file found, in the same order `solution
/// export` would have written them (choices first, then each table folder's
/// own table, then its views, then its forms).
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
/// required (not optional the way it is on the individual `table import`/
/// `choice import` commands) and is likewise forwarded to every table/
/// choice import, so any brand-new column/lookup relationship/global choice
/// this run creates actually joins the solution it came from — see
/// <see cref="Services.Dataverse.IDataverseClient.CreateAttributeAsync"/>'s
/// own doc comment for the gap this closes; without it, a new asset would
/// exist live but silently not show up in a later `solution export` of the
/// same solution.
///
/// One file failing (a validation error, a Dataverse rejection, a missing
/// live counterpart) doesn't stop the rest — this keeps going through every
/// remaining file so one bad form doesn't block importing everything else,
/// then reports how many succeeded/failed at the end and exits non-zero if
/// anything did. There's no cross-file rollback: each asset's own write is
/// already atomic on its own terms (`table import`'s changeset, in
/// particular), but nothing ties multiple files' writes together into one
/// larger transaction.
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

        var succeeded = 0;
        var failed = 0;

        var choicesPath = Path.Combine(settings.Input, "choices.yml");
        if (File.Exists(choicesPath))
        {
            var result = await new ImportChoiceCommand(globalChoiceImportService, importRunner).RunAsync(
                new ImportChoiceCommand.Settings { Input = choicesPath, Yes = settings.Yes, WhatIf = settings.WhatIf, Solution = settings.Solution }, cancellationToken);
            if (result == 0) succeeded++; else failed++;
            AnsiConsole.WriteLine();
        }

        foreach (var entityDirectory in Directory.GetDirectories(settings.Input).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var tablePath in Directory.GetFiles(entityDirectory, "*.table.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var result = await new ImportTableCommand(tableImportService, importRunner).RunAsync(
                    new ImportTableCommand.Settings { Input = tablePath, Yes = settings.Yes, WhatIf = settings.WhatIf, NoTransaction = settings.NoTransaction, Solution = settings.Solution }, cancellationToken);
                if (result == 0) succeeded++; else failed++;
                AnsiConsole.WriteLine();
            }

            foreach (var viewPath in Directory.GetFiles(entityDirectory, "*.view.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var result = await new ImportViewCommand(viewImportService, importRunner).RunAsync(
                    new ImportViewCommand.Settings { Input = viewPath, Yes = settings.Yes, WhatIf = settings.WhatIf }, cancellationToken);
                if (result == 0) succeeded++; else failed++;
                AnsiConsole.WriteLine();
            }

            foreach (var formPath in Directory.GetFiles(entityDirectory, "*.form.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var result = await new ImportFormCommand(formImportService, importRunner).RunAsync(
                    new ImportFormCommand.Settings { Input = formPath, Yes = settings.Yes, WhatIf = settings.WhatIf, AllowSchemaViolations = settings.AllowSchemaViolations }, cancellationToken);
                if (result == 0) succeeded++; else failed++;
                AnsiConsole.WriteLine();
            }
        }

        if (succeeded == 0 && failed == 0)
        {
            ErrorConsole.Warn($"No choices.yml or table sub-folders found under '{settings.Input}' — nothing to import.");
            return 0;
        }

        AnsiConsole.MarkupLine(failed == 0
            ? $"[green]Done.[/] {succeeded} asset(s) processed."
            : $"[yellow]Done with errors.[/] {succeeded} succeeded, {failed} failed — see above.");

        return failed == 0 ? 0 : 1;
    }
}

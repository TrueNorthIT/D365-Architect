using System.ComponentModel;
using D365Architect.Commands;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Table;

/// <summary>
/// `d365architect table import --input account.table.yml [--yes] [--whatif] [--no-transaction] [--solution examplesolution]`
/// Writes a `*.table.yml` file's table-level properties
/// (<c>DisplayName</c>/<c>PluralDisplayName</c>/<c>Description</c>) and
/// columns back into Dataverse. Needs sign-in.
///
/// Pass <c>--solution</c> to add any brand-new column (or lookup
/// relationship) this import creates to that solution as part of the same
/// write — confirmed live as a real gap otherwise: without it, a new column
/// lands wherever Dataverse's own default solution context puts it (in
/// practice, the Default Solution), not the solution this file came from,
/// so a later solution-scoped export silently wouldn't show it. Never
/// affects an update to a column that already exists.
///
/// Before writing anything: prints the full YAML diff between the local
/// file and re-exporting the table right now (informational — everything
/// that's different), plus a separate, explicit per-column plan of what
/// will actually happen (see <see cref="AttributeImportAction"/>) — a
/// column can show up as different in the YAML diff without anything being
/// done about it, when its type isn't one this tool can safely create or
/// update yet (see <see cref="Services.Dataverse.AttributeMetadataJsonBuilder.CreatableTypes"/>/
/// <see cref="Services.Dataverse.AttributeMetadataJsonBuilder.SupportedTypes"/>),
/// or when it's live but missing from the local YAML (never auto-deleted).
/// Nothing is written until you confirm (or pass <c>--yes</c>), and if
/// there's nothing to actually do, nothing is written at all. Pass
/// <c>--whatif</c> to see just the diff/plan and never be prompted or write
/// anything at all.
///
/// Never creates the table itself if it doesn't exist yet.
///
/// Every write in the column plan (plus the table-level update, if any) is
/// sent as one atomic Dataverse changeset by default — either all of it
/// takes, or (on any single failure) none of it does, rather than leaving
/// the table with only some of the plan applied. Pass
/// <c>--no-transaction</c> to send them one request at a time instead, same
/// as this tool always did before that existed — useful if an environment
/// turns out to reject batched metadata writes, or if partial progress on
/// failure is actually what you want.
///
/// What this doesn't do yet: publish the change — Dataverse customizations
/// still need publishing separately before end users see it (confirmed
/// required for table/column changes specifically, unlike form/view
/// import's still-open question — see `docs/yaml-conventions.md`).
///
/// The shared preview → diff → confirm → apply flow itself lives in the
/// injected <see cref="ImportRunner"/>, alongside `form import`/`view
/// import` — this class supplies only what's actually different about a
/// table: how to read/preview/apply it, its column plan, and its own
/// exceptions.
/// </summary>
public sealed class ImportTableCommand(ITableImportService tableImportService, ImportRunner importRunner)
    : AsyncCommand<ImportTableCommand.Settings>
{
    public sealed class Settings : ImportSettingsBase
    {
        [CommandOption("--no-transaction")]
        [Description("Send every column create/update one request at a time instead of as a single atomic Dataverse changeset. Use this if the environment rejects batched metadata writes, or to keep whatever succeeded before a later one fails rather than having the whole import rolled back.")]
        public bool NoTransaction { get; init; }

        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of a solution to add any brand-new column (or lookup relationship) to as it's created. Never affects updates to an existing column. Omit to leave a new column wherever Dataverse's own default solution context puts it, same as before this option existed.")]
        public string? Solution { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    /// <summary>
    /// The command's own logic, independent of Spectre's <see cref="CommandContext"/> —
    /// pulled out of <see cref="ExecuteAsync"/> so <c>solution import</c> can
    /// run this exact same preview → diff → confirm → apply flow for every
    /// <c>*.table.yml</c> it finds, without duplicating any of it.
    /// </summary>
    public async Task<int> RunAsync(Settings settings, CancellationToken cancellationToken)
    {
        var spec = new ImportFlowSpec<Settings, EntityDefinition, TableImportPreview>
        {
            ReadInputAsync = (s, ct) => YamlFileReader.TryReadAsync(s.Input, "table", EntityYamlDeserializer.FromYaml, ct),

            SubjectName = entity => entity.LogicalName,

            PreviewStatusMessage = entity => $"Looking up '{entity.LogicalName}'...",

            SkipBeforePrinting = preview => !preview.HasChanges && GetActionable(preview).Count == 0
                ? "the local YAML already matches what's live in Dataverse. Nothing to import."
                : null,

            PrintChanges = preview =>
            {
                DiffConsole.PrintDiff(TextDiff.Compute(preview.ExistingYaml, preview.NewYaml));
                AnsiConsole.WriteLine();

                var actionable = GetActionable(preview);
                if (actionable.Count > 0)
                {
                    AnsiConsole.MarkupLine("[bold]Column plan:[/]");
                    foreach (var plan in actionable)
                    {
                        PrintPlanLine(plan);
                    }

                    AnsiConsole.WriteLine();
                }
            },

            SkipAfterPrinting = preview => preview.HasChanges
                ? null
                : "[yellow]Nothing above is actually applicable[/] (every difference shown is on an unsupported column type, an invalid change, or a column this tool won't delete). Nothing to import.",

            ApplyStatusMessage = "Importing...",

            ApplyAsync = (auth, preview, ct) => tableImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, useTransaction: !settings.NoTransaction, settings.Solution, ct),

            PrintSuccess = (entity, preview) =>
            {
                AnsiConsole.MarkupLine($"[green]Imported.[/] '{entity.LogicalName}' updated in Dataverse.");
                AnsiConsole.MarkupLine("[grey]Note: this only updates Dataverse's metadata — publish customizations separately (e.g. in the maker portal) before end users see the change; this tool doesn't publish yet.[/]");
            },

            FormatDomainException = (ex, entity) => ex switch
            {
                InvalidDataException => $"[red]Couldn't parse the live metadata for '{entity.LogicalName}':[/] {ex.Message.EscapeMarkup()}",
                _ => null,
            },
        };

        return await importRunner.RunAsync(settings, tableImportService, spec, cancellationToken);
    }

    /// <summary>
    /// A column can show up as different in the YAML diff without anything
    /// actually being done about it (unsupported type, an invalid change, a
    /// column this tool won't delete) — this is what's left once those are
    /// filtered out, driving both the column plan and the two "nothing to
    /// do" checks around it.
    /// </summary>
    private static List<AttributeImportPlan> GetActionable(TableImportPreview preview)
        => preview.AttributePlans.Where(p => p.Action != AttributeImportAction.Unchanged).ToList();

    private static void PrintPlanLine(AttributeImportPlan plan)
    {
        var line = plan.Action switch
        {
            AttributeImportAction.Create => $"[green]  + {plan.LogicalName.EscapeMarkup()} (create)[/]",
            AttributeImportAction.Update => $"[yellow]  ~ {plan.LogicalName.EscapeMarkup()} (update)[/]",
            AttributeImportAction.CreateLookupRelationship => $"[green]  + {plan.LogicalName.EscapeMarkup()} (create lookup relationship)[/]",
            AttributeImportAction.CreateCustomerRelationship => $"[green]  + {plan.LogicalName.EscapeMarkup()} (create customer relationship)[/]",
            AttributeImportAction.SkippedUnsupportedType => $"[grey]  ? {plan.LogicalName.EscapeMarkup()} (not applied: {plan.Reason?.EscapeMarkup()})[/]",
            AttributeImportAction.WouldRemove => $"[red]  - {plan.LogicalName.EscapeMarkup()} (not applied: {plan.Reason?.EscapeMarkup()})[/]",
            AttributeImportAction.Invalid => $"[red]  ! {plan.LogicalName.EscapeMarkup()} (not applied: {plan.Reason?.EscapeMarkup()})[/]",
            _ => $"  {plan.LogicalName.EscapeMarkup()}",
        };

        AnsiConsole.MarkupLine(line);

        if (plan.OptionChanges is { Count: > 0 })
        {
            foreach (var change in plan.OptionChanges)
            {
                var glyph = change.Action switch
                {
                    OptionChangeAction.InsertOption or OptionChangeAction.InsertStatusValue => "+",
                    OptionChangeAction.OrderOptions => "↕",
                    _ => "~",
                };
                AnsiConsole.MarkupLine($"[yellow]      {glyph} {change.Description.EscapeMarkup()}[/]");
            }
        }

        if (plan.Warnings is { Count: > 0 })
        {
            foreach (var warning in plan.Warnings)
            {
                ErrorConsole.Warn($"      ⚠ {warning}");
            }
        }
    }
}

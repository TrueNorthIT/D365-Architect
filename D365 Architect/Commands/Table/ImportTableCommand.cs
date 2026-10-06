using System.ComponentModel;
using D365Architect.Commands;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Table;

/// <summary>
/// `d365architect table import --input account.table.yml [--yes] [--whatif]`
/// Writes a `*.table.yml` file's table-level properties
/// (<c>DisplayName</c>/<c>PluralDisplayName</c>/<c>Description</c>) and
/// columns back into Dataverse. Needs sign-in.
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
/// That last case — a live-only column, e.g. Dataverse's own auto-generated
/// <c>...name</c>/<c>...yominame</c> companions — is universal (every table
/// has some) rather than exceptional, so it's never enough on its own to
/// keep this from reporting "no changes"; it's summarized as a single count
/// instead of listed, unless <c>--show-unmanaged</c> is passed. Nothing is
/// written until you confirm (or pass <c>--yes</c>), and if there's nothing
/// to actually do, nothing is written at all. Pass <c>--whatif</c> to see
/// just the diff/plan and never be prompted or write anything at all.
///
/// Never creates the table itself if it doesn't exist yet.
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
        [CommandOption("--show-unmanaged")]
        [Description("Also list every live column absent from the local YAML (companion columns like ...name/...yominame included) instead of just a count. These are never deleted by this tool either way.")]
        public bool ShowUnmanaged { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var spec = new ImportFlowSpec<Settings, EntityDefinition, TableImportPreview>
        {
            ReadInputAsync = (s, ct) => YamlFileReader.TryReadAsync(s.Input, "table", EntityYamlDeserializer.FromYaml, ct),

            SubjectName = entity => entity.LogicalName,

            PreviewStatusMessage = entity => $"Looking up '{entity.LogicalName}'...",

            // WouldRemove doesn't count towards "is there anything to
            // report" — every table always carries live-only columns this
            // tool never models (Dataverse's own auto-generated companions),
            // so treating them as actionable meant this could never fire on
            // any real table (see GetActionable). Genuinely clean files now
            // get the short, plain message instead of the full diff + a
            // dozens-of-lines "Column plan".
            SkipBeforePrinting = preview =>
            {
                if (preview.HasChanges || GetActionable(preview).Count > 0)
                {
                    return null;
                }

                var unmanagedCount = GetUnmanaged(preview).Count;
                return unmanagedCount == 0
                    ? "the local YAML already matches what's live in Dataverse. Nothing to import."
                    : $"the local YAML already matches what's live in Dataverse. Nothing to import. ({unmanagedCount} live column{(unmanagedCount == 1 ? "" : "s")} not in this file {(unmanagedCount == 1 ? "is" : "are")} left alone — never deleted automatically.)";
            },

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

                PrintUnmanagedSummary(GetUnmanaged(preview), settings.ShowUnmanaged);
            },

            SkipAfterPrinting = preview => preview.HasChanges
                ? null
                : "[yellow]Nothing above is actually applicable[/] (every difference shown is on an unsupported column type or an invalid change). Nothing to import.",

            ApplyStatusMessage = "Importing...",

            ApplyAsync = (auth, preview, ct) => tableImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, ct),

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
    /// actually being done about it (unsupported type, an invalid change) —
    /// this is what's left once <see cref="AttributeImportAction.Unchanged"/>
    /// and <see cref="AttributeImportAction.WouldRemove"/> are filtered out,
    /// driving the "Column plan" section and the two "nothing to do" checks
    /// around it. WouldRemove is deliberately excluded here — see
    /// <see cref="GetUnmanaged"/> — because it's present on essentially
    /// every table (Dataverse's own auto-generated companion columns) and
    /// isn't something this tool will ever act on regardless.
    /// </summary>
    private static List<AttributeImportPlan> GetActionable(TableImportPreview preview)
        => preview.AttributePlans.Where(p => p.Action is not (AttributeImportAction.Unchanged or AttributeImportAction.WouldRemove)).ToList();

    /// <summary>Live columns absent from the local YAML — never deleted, and never "actionable" (see <see cref="GetActionable"/>), just demoted to a count unless <c>--show-unmanaged</c> is passed.</summary>
    private static List<AttributeImportPlan> GetUnmanaged(TableImportPreview preview)
        => preview.AttributePlans.Where(p => p.Action == AttributeImportAction.WouldRemove).ToList();

    private static void PrintUnmanagedSummary(List<AttributeImportPlan> unmanaged, bool showUnmanaged)
    {
        if (unmanaged.Count == 0)
        {
            return;
        }

        if (!showUnmanaged)
        {
            AnsiConsole.MarkupLine($"[grey]{unmanaged.Count} live column{(unmanaged.Count == 1 ? "" : "s")} not in this file — never deleted automatically. Pass --show-unmanaged to list them.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        AnsiConsole.MarkupLine("[bold]Unmanaged columns[/] [grey](live, not in this file — never deleted automatically):[/]");
        foreach (var plan in unmanaged)
        {
            PrintPlanLine(plan);
        }

        AnsiConsole.WriteLine();
    }

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

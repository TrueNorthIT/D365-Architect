using System.ComponentModel;
using D365Architect.Commands;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Table;

/// <summary>
/// `d365architect table import --input account.table.yml [--yes]`
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
/// update yet (see <see cref="Services.Dataverse.AttributeMetadataJsonBuilder.SupportedTypes"/>),
/// or when it's live but missing from the local YAML (never auto-deleted).
/// Nothing is written until you confirm (or pass <c>--yes</c>), and if
/// there's nothing to actually do, nothing is written at all.
///
/// Never creates the table itself if it doesn't exist yet.
///
/// What this doesn't do yet: publish the change — Dataverse customizations
/// still need publishing separately before end users see it (confirmed
/// required for table/column changes specifically, unlike form/view
/// import's still-open question — see `docs/yaml-conventions.md`).
/// </summary>
public sealed class ImportTableCommand(IAuthenticationService authenticationService, ITableImportService tableImportService)
    : AsyncCommand<ImportTableCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <PATH>")]
        [Description("Path to the *.table.yml file to import.")]
        public required string Input { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt and import immediately.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var entity = await EntityYamlFileReader.TryReadAsync(settings.Input, cancellationToken);
        if (entity is null)
        {
            return 1;
        }

        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var preview = await AnsiConsole.Status().StartAsync($"Looking up '{entity.LogicalName}'...",
                async _ => await tableImportService.PreviewAsync(auth.EnvironmentUrl, auth.AccessToken, entity, cancellationToken));

            var actionable = preview.AttributePlans.Where(p => p.Action != AttributeImportAction.Unchanged).ToList();
            if (!preview.HasChanges && actionable.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]No changes[/] — the local YAML already matches what's live in Dataverse. Nothing to import.");
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]Changes for '{entity.LogicalName}':[/]");
            DiffConsole.PrintDiff(TextDiff.Compute(preview.ExistingYaml, preview.NewYaml));
            AnsiConsole.WriteLine();

            if (actionable.Count > 0)
            {
                AnsiConsole.MarkupLine("[bold]Column plan:[/]");
                foreach (var plan in actionable)
                {
                    PrintPlanLine(plan);
                }

                AnsiConsole.WriteLine();
            }

            if (!preview.HasChanges)
            {
                AnsiConsole.MarkupLine("[yellow]Nothing above is actually applicable[/] (every difference shown is on an unsupported column type, an invalid change, or a column this tool won't delete). Nothing to import.");
                return 0;
            }

            if (!settings.Yes && !AnsiConsole.Confirm("Import these changes into Dataverse?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Aborted.[/] Nothing was written.");
                return 0;
            }

            await AnsiConsole.Status().StartAsync("Importing...",
                async _ => await tableImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, cancellationToken));

            AnsiConsole.MarkupLine($"[green]Imported.[/] '{entity.LogicalName}' updated in Dataverse.");
            AnsiConsole.MarkupLine("[grey]Note: this only updates Dataverse's metadata — publish customizations separately (e.g. in the maker portal) before end users see the change; this tool doesn't publish yet.[/]");
            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
        catch (InvalidDataException ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't parse the live metadata for '{entity.LogicalName}':[/] {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
    }

    private static void PrintPlanLine(AttributeImportPlan plan)
    {
        var line = plan.Action switch
        {
            AttributeImportAction.Create => $"[green]  + {plan.LogicalName.EscapeMarkup()} (create)[/]",
            AttributeImportAction.Update => $"[yellow]  ~ {plan.LogicalName.EscapeMarkup()} (update)[/]",
            AttributeImportAction.SkippedUnsupportedType => $"[grey]  ? {plan.LogicalName.EscapeMarkup()} (not applied: {plan.Reason?.EscapeMarkup()})[/]",
            AttributeImportAction.WouldRemove => $"[red]  - {plan.LogicalName.EscapeMarkup()} (not applied: {plan.Reason?.EscapeMarkup()})[/]",
            AttributeImportAction.Invalid => $"[red]  ! {plan.LogicalName.EscapeMarkup()} (not applied: {plan.Reason?.EscapeMarkup()})[/]",
            _ => $"  {plan.LogicalName.EscapeMarkup()}",
        };

        AnsiConsole.MarkupLine(line);

        if (plan.Warnings is { Count: > 0 })
        {
            foreach (var warning in plan.Warnings)
            {
                AnsiConsole.MarkupLine($"[yellow]      ⚠ {warning.EscapeMarkup()}[/]");
            }
        }
    }
}

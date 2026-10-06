using System.ComponentModel;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Choice;

/// <summary>
/// `d365architect choice import --input choices.yml [--yes] [--whatif] [--solution examplesolution]`
/// Writes a `*.choice.yml` file's global choices back into Dataverse —
/// creating any choice that doesn't exist live yet, unlike `table import`
/// (which never creates the table itself; see
/// <see cref="IGlobalChoiceImportService"/>'s own doc comment for why a
/// global choice is different).
///
/// Pass <c>--solution</c> to add any brand-new choice this import creates
/// to that solution as part of the same write — confirmed live as a real
/// gap otherwise: without it, a new choice lands wherever Dataverse's own
/// default solution context puts it, not the solution this file came from,
/// so a later solution-scoped export silently wouldn't show it. Never
/// affects an update to a choice that already exists.
///
/// Before writing anything: prints the full YAML diff between the local
/// file and re-exporting just the named choices right now (informational —
/// everything that's different), plus a separate, explicit per-choice plan
/// of what will actually happen (see <see cref="GlobalChoiceImportAction"/>)
/// — same structure as `table import`'s own YAML diff + column plan.
/// Nothing is written until you confirm (or pass `--yes`); pass `--whatif`
/// to see just the diff/plan and never be prompted or write anything at
/// all.
///
/// What this doesn't do yet: publish the change — Dataverse customizations
/// still need publishing separately before end users see it, same as
/// `table import`.
/// </summary>
public sealed class ImportChoiceCommand(IGlobalChoiceImportService globalChoiceImportService, ImportRunner importRunner)
    : AsyncCommand<ImportChoiceCommand.Settings>
{
    public sealed class Settings : ImportSettingsBase
    {
        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of a solution to add any brand-new global choice to as it's created. Never affects updates to an existing choice. Omit to leave a new choice wherever Dataverse's own default solution context puts it, same as before this option existed.")]
        public string? Solution { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    /// <summary>
    /// The command's own logic, independent of Spectre's <see cref="CommandContext"/> —
    /// pulled out of <see cref="ExecuteAsync"/> so <c>solution import</c> can
    /// run this exact same preview → diff → confirm → apply flow for a
    /// solution's <c>choices.yml</c>, without duplicating any of it.
    /// </summary>
    public async Task<int> RunAsync(Settings settings, CancellationToken cancellationToken)
    {
        var spec = new ImportFlowSpec<Settings, IReadOnlyList<GlobalChoiceDefinition>, GlobalChoicesImportPreview>
        {
            ReadInputAsync = (s, ct) => YamlFileReader.TryReadAsync(s.Input, "global choice list", GlobalChoiceYamlDeserializer.FromYaml, ct),

            SubjectName = choices => choices.Count == 1 ? choices[0].Name : $"{choices.Count} global choices",

            PreviewStatusMessage = choices => $"Looking up {choices.Count} global choice{(choices.Count == 1 ? "" : "s")}...",

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
                    AnsiConsole.MarkupLine("[bold]Choice plan:[/]");
                    foreach (var plan in actionable)
                    {
                        PrintPlanLine(plan);
                    }

                    AnsiConsole.WriteLine();
                }
            },

            SkipAfterPrinting = preview => preview.HasChanges
                ? null
                : "[yellow]Nothing above is actually applicable[/] (every difference shown is on an invalid change). Nothing to import.",

            ApplyStatusMessage = "Importing...",

            ApplyAsync = (auth, preview, ct) => globalChoiceImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, settings.Solution, ct),

            PrintSuccess = (choices, preview) =>
            {
                var created = preview.ChoicePlans.Count(p => p.Action == GlobalChoiceImportAction.Create);
                var updated = preview.ChoicePlans.Count(p => p.Action == GlobalChoiceImportAction.Update);
                AnsiConsole.MarkupLine($"[green]Imported.[/] {created} created, {updated} updated in Dataverse.");
                AnsiConsole.MarkupLine("[grey]Note: this only updates Dataverse's metadata — publish customizations separately (e.g. in the maker portal) before end users see the change; this tool doesn't publish yet.[/]");
            },

            FormatDomainException = (ex, choices) => ex switch
            {
                InvalidDataException => $"[red]Couldn't parse the live metadata:[/] {ex.Message.EscapeMarkup()}",
                _ => null,
            },
        };

        return await importRunner.RunAsync(settings, globalChoiceImportService, spec, cancellationToken);
    }

    /// <summary>
    /// A choice can show up as different in the YAML diff without anything
    /// actually being done about it (an invalid change) — this is what's
    /// left once that's filtered out, driving both the choice plan and the
    /// two "nothing to do" checks around it. Same pattern as `table
    /// import`'s own GetActionable.
    /// </summary>
    private static List<GlobalChoiceImportPlan> GetActionable(GlobalChoicesImportPreview preview)
        => preview.ChoicePlans.Where(p => p.Action != GlobalChoiceImportAction.Unchanged).ToList();

    private static void PrintPlanLine(GlobalChoiceImportPlan plan)
    {
        var line = plan.Action switch
        {
            GlobalChoiceImportAction.Create => $"[green]  + {plan.Name.EscapeMarkup()} (create)[/]",
            GlobalChoiceImportAction.Update => $"[yellow]  ~ {plan.Name.EscapeMarkup()} (update)[/]",
            GlobalChoiceImportAction.Invalid => $"[red]  ! {plan.Name.EscapeMarkup()} (not applied: {plan.Reason?.EscapeMarkup()})[/]",
            _ => $"  {plan.Name.EscapeMarkup()}",
        };

        AnsiConsole.MarkupLine(line);

        if (plan.OptionChanges is { Count: > 0 })
        {
            foreach (var change in plan.OptionChanges)
            {
                var glyph = change.Action switch
                {
                    OptionChangeAction.InsertOption => "+",
                    OptionChangeAction.OrderOptions => "↕",
                    _ => "~",
                };
                AnsiConsole.MarkupLine($"[yellow]      {glyph} {change.Description.EscapeMarkup()}[/]");
            }
        }
    }
}

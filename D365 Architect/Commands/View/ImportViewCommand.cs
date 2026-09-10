using D365Architect.Commands;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.View;

/// <summary>
/// `d365architect view import --input account-active.view.yml [--yes] [--whatif]`
/// Writes a `*.view.yml` file's Description/FetchXml/LayoutXml directly
/// back into Dataverse. Needs sign-in.
///
/// Simpler than `form import`: a view's FetchXml/LayoutXml are kept
/// verbatim (see <see cref="Services.Conversion.Models.ViewDefinition"/>'s
/// own doc comment), never decomposed and rebuilt through a writer, so
/// there's no id-resynthesis to cancel out before diffing — the live
/// values are compared directly against the local YAML.
///
/// Only ever updates a view that already exists — refuses (rather than
/// creating one) when no view matches the YAML's table + name yet.
/// `QueryType`/`IsDefault`/`IsQuickFindQuery` are never written — see
/// <see cref="IViewImportService"/>'s own doc comment for why.
///
/// What this doesn't do yet: publish the change (Dataverse customizations
/// still need publishing separately before end users see it).
///
/// The shared preview → diff → confirm → apply flow itself lives in the
/// injected <see cref="ImportRunner"/>, alongside `form import`/`table
/// import` — this class supplies only what's actually different about a
/// view: how to read/preview/apply it, its three separate field diffs, and
/// its own exceptions.
/// </summary>
public sealed class ImportViewCommand(IViewImportService viewImportService, ImportRunner importRunner)
    : AsyncCommand<ImportViewCommand.Settings>
{
    public sealed class Settings : ImportSettingsBase;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var spec = new ImportFlowSpec<Settings, ViewDefinition, ViewImportPreview>
        {
            ReadInputAsync = (s, ct) => YamlFileReader.TryReadAsync(s.Input, "view", ViewYamlDeserializer.FromYaml, ct),

            SubjectName = view => view.Name,

            PreviewStatusMessage = view => $"Looking up '{view.Name}'...",

            SkipBeforePrinting = preview => preview.HasChanges
                ? null
                : "the local YAML already matches what's live in Dataverse. Nothing to import.",

            PrintChanges = preview =>
            {
                PrintFieldDiff("description", preview.ExistingDescription, preview.NewDescription, pretty: false);
                PrintFieldDiff("fetchxml", preview.ExistingFetchXml, preview.NewFetchXml, pretty: true);
                PrintFieldDiff("layoutxml", preview.ExistingLayoutXml, preview.NewLayoutXml, pretty: true);
                AnsiConsole.WriteLine();
            },

            ApplyStatusMessage = "Importing...",

            ApplyAsync = (auth, preview, ct) => viewImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, ct),

            PrintSuccess = (view, preview) =>
            {
                AnsiConsole.MarkupLine($"[green]Imported.[/] '{view.Name}' updated in Dataverse.");
                AnsiConsole.MarkupLine("[grey]Note: this only updates the view's own fields — publish customizations separately (e.g. in the maker portal) before end users see the change; this tool doesn't publish yet.[/]");
            },

            FormatDomainException = (ex, view) => ex switch
            {
                ViewNotFoundException or AmbiguousSavedQueryException => $"[red]{ex.Message.EscapeMarkup()}[/]",
                _ => null,
            },
        };

        return await importRunner.RunAsync(settings, viewImportService, spec, cancellationToken);
    }

    /// <summary>
    /// Prints one field's diff (skipping it entirely when the local YAML
    /// never had a value there, or when the two sides already match) —
    /// unlike form/table import, a view has exactly three writable fields
    /// total, so showing each by name is clearer than merging them into one
    /// combined block.
    /// </summary>
    private static void PrintFieldDiff(string fieldName, string? existingValue, string? newValue, bool pretty)
    {
        if (newValue is null || newValue == existingValue)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{fieldName}:[/]");

        var existingText = pretty && existingValue is not null ? DiffConsole.PrettyPrintXml(existingValue) : existingValue ?? "";
        var newText = pretty ? DiffConsole.PrettyPrintXml(newValue) : newValue;
        DiffConsole.PrintDiff(TextDiff.Compute(existingText, newText));
    }
}

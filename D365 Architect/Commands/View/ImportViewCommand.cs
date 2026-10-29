using System.ComponentModel;
using D365Architect.Commands;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.View;

/// <summary>
/// `d365architect view import --input account-active.view.yml [--solution UNIQUE_NAME] [--yes] [--whatif]`
/// Writes a `*.view.yml` file's Description/FetchXml/LayoutXml directly
/// back into Dataverse. Needs sign-in.
///
/// Simpler than `form import`: a view's FetchXml/LayoutXml are kept
/// verbatim (see <see cref="Services.Conversion.Models.ViewDefinition"/>'s
/// own doc comment), never decomposed and rebuilt through a writer, so
/// there's no id-resynthesis to cancel out before diffing — the live
/// values are compared directly against the local YAML.
///
/// A view matching nothing by the YAML's table + name is created — shown in
/// the plan as `+ view '&lt;name&gt;' (create)` and confirmed like any other
/// change; a stale `savedQueryId` is still an error. A hand-written file may
/// list `columns` instead of a raw `layoutXml` (see
/// <see cref="IViewImportService"/>). `--solution` adds whatever is created
/// or changed to that solution. `QueryType`/`IsDefault`/`IsQuickFindQuery`
/// are never written — see <see cref="IViewImportService"/>'s own doc
/// comment for why.
///
/// Once the change is written, the view's table is published (Dataverse
/// publishes a view as part of its table) unless <c>--no-publish</c> —
/// and confirmed live, that gap isn't just cosmetic for verification: a
/// plain <c>savedqueries</c> GET reflects the *published* state, so a
/// follow-up `view export` won't show a just-written FetchXml/LayoutXml
/// change until something publishes the table (same platform behavior
/// documented on <see cref="Services.Dataverse.IDataverseClient.UpdateSavedQueryAsync(Uri, string, Guid, string?, string?, string?, CancellationToken)"/>,
/// confirmed for forms first — see <see cref="Services.Dataverse.IDataverseClient.UpdateSystemFormXmlAsync(Uri, string, Guid, string, CancellationToken)"/>).
/// The write itself genuinely takes regardless.
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
    public sealed class Settings : PublishableImportSettings
    {
        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of a solution to add the view to as it is created or changed.")]
        public string? Solution { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    /// <summary>
    /// The command's own logic, independent of Spectre's <see cref="CommandContext"/> —
    /// pulled out of <see cref="ExecuteAsync"/> so <c>solution import</c> can
    /// run this exact same preview → diff → confirm → apply flow for every
    /// <c>*.view.yml</c> it finds, without duplicating any of it.
    /// </summary>
    public async Task<int> RunAsync(Settings settings, CancellationToken cancellationToken)
    {
        var spec = new ImportFlowSpec<Settings, ViewDefinition, ViewImportPreview>
        {
            ReadInputAsync = (s, ct) => YamlFileReader.TryReadAsync(s.Input, "view", ViewYamlDeserializer.FromYaml, ct),

            SubjectName = view => view.Name,

            PreviewStatusMessage = view => $"Looking up '{view.Name}'...",

            OnPreviewBuilt = preview =>
            {
                if (preview.IdentityMismatchWarning is not null)
                {
                    ErrorConsole.Warn($"Warning: {preview.IdentityMismatchWarning}");
                    AnsiConsole.WriteLine();
                }
            },

            SkipBeforePrinting = preview => preview.HasChanges
                ? null
                : "the local YAML already matches what's live in Dataverse. Nothing to import.",

            PrintChanges = preview =>
            {
                if (preview.IsCreate)
                {
                    AnsiConsole.MarkupLine($"[green]{$"+ view '{preview.Name}' (create)".EscapeMarkup()}[/]");
                }

                PrintFieldDiff("description", preview.ExistingDescription, preview.NewDescription, pretty: false);
                PrintFieldDiff("fetchxml", preview.ExistingFetchXml, preview.NewFetchXml, pretty: true);
                PrintFieldDiff("layoutxml", preview.ExistingLayoutXml, preview.NewLayoutXml, pretty: true);
                AnsiConsole.WriteLine();
            },

            ApplyStatusMessage = "Importing...",

            ApplyAsync = (auth, preview, ct) => viewImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, settings.Solution, ct),

            PrintSuccess = (view, preview) =>
            {
                AnsiConsole.MarkupLine($"[green]Imported.[/] '{view.Name.EscapeMarkup()}' {(preview.IsCreate ? "created" : "updated")} in Dataverse.");
                if (settings.NoPublish)
                {
                    AnsiConsole.MarkupLine("[grey]Note: --no-publish — publish customizations separately (e.g. in the maker portal) before end users see the change. Until you do, a follow-up 'view export' won't show this change either — it reads the same unpublished state, not a sign the import failed.[/]");
                }
            },

            FormatDomainException = (ex, view) => ex switch
            {
                ViewNotFoundException or AmbiguousSavedQueryException or ViewIdentityMismatchException or InvalidDataException => $"[red]{ex.Message.EscapeMarkup()}[/]",
                _ => null,
            },

            // Dataverse publishes a view as part of its table.
            PublishAsync = (auth, preview, ct) => viewImportService.PublishAsync(auth.EnvironmentUrl, auth.AccessToken, preview, ct),
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

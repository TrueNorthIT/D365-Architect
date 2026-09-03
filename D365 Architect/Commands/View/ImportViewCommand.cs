using System.ComponentModel;
using D365Architect.Commands;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.View;

/// <summary>
/// `d365architect view import --input account-active.view.yml [--yes]`
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
/// </summary>
public sealed class ImportViewCommand(IAuthenticationService authenticationService, IViewImportService viewImportService)
    : AsyncCommand<ImportViewCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <PATH>")]
        [Description("Path to the *.view.yml file to import.")]
        public required string Input { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt and import immediately.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var view = await ViewYamlFileReader.TryReadAsync(settings.Input, cancellationToken);
        if (view is null)
        {
            return 1;
        }

        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var preview = await AnsiConsole.Status().StartAsync($"Looking up '{view.Name}'...",
                async _ => await viewImportService.PreviewAsync(auth.EnvironmentUrl, auth.AccessToken, view, cancellationToken));

            if (!preview.HasChanges)
            {
                AnsiConsole.MarkupLine("[green]No changes[/] — the local YAML already matches what's live in Dataverse. Nothing to import.");
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]Changes for '{view.Name}':[/]");
            PrintFieldDiff("description", preview.ExistingDescription, preview.NewDescription, pretty: false);
            PrintFieldDiff("fetchxml", preview.ExistingFetchXml, preview.NewFetchXml, pretty: true);
            PrintFieldDiff("layoutxml", preview.ExistingLayoutXml, preview.NewLayoutXml, pretty: true);
            AnsiConsole.WriteLine();

            if (!settings.Yes && !AnsiConsole.Confirm("Import these changes into Dataverse?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Aborted.[/] Nothing was written.");
                return 0;
            }

            await AnsiConsole.Status().StartAsync("Importing...",
                async _ => await viewImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, cancellationToken));

            AnsiConsole.MarkupLine($"[green]Imported.[/] '{view.Name}' updated in Dataverse.");
            AnsiConsole.MarkupLine("[grey]Note: this only updates the view's own fields — publish customizations separately (e.g. in the maker portal) before end users see the change; this tool doesn't publish yet.[/]");
            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (ViewNotFoundException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (AmbiguousSavedQueryException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
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

using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.View;

/// <summary>
/// `d365architect view export --table account [--solution examplesolution] [--output views]`
/// Fetches every view defined on a table from the currently signed-in D365
/// environment and saves each as its own file of this tool's declarative
/// YAML.
/// </summary>
public sealed class ExportViewCommand(IAuthenticationService authenticationService, IViewExportService viewExportService)
    : AsyncCommand<ExportViewCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-t|--table <LOGICAL_NAME>")]
        [Description("Logical name of the table whose views to export, e.g. account")]
        public required string Table { get; init; }

        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Scope the export to only the views this solution customizes, instead of every view on the table.")]
        public string? Solution { get; init; }

        [CommandOption("-o|--output <DIRECTORY>")]
        [Description("Directory to write the exported YAML files into. Defaults to the current directory.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var views = await AnsiConsole.Status().StartAsync($"Exporting views for '{settings.Table}'...",
                async _ => await viewExportService.ExportViewsAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Table, settings.Solution, cancellationToken));

            if (views.Count == 0)
            {
                ErrorConsole.Warn($"No views found for '{settings.Table}'.");
                return 0;
            }

            var outputDirectory = settings.Output ?? ".";
            Directory.CreateDirectory(outputDirectory);

            // "{name}.{asset type}.yml" — the same naming convention `table
            // export` follows, so a folder of exported YAML stays sortable
            // and unambiguous across asset types.
            foreach (var view in views)
            {
                var outputPath = Path.Combine(outputDirectory, $"{view.FileNameStem}.view.yml");
                await File.WriteAllTextAsync(outputPath, view.Yaml, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {outputPath}");
            }

            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (SolutionNotFoundException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (InvalidDataException ex)
        {
            ErrorConsole.Print($"Couldn't parse the view metadata for '{settings.Table}': {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
    }
}

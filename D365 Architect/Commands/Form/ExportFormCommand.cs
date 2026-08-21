using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Form;

/// <summary>
/// `d365architect form export --table account [--solution examplesolution] [--output forms]`
/// Fetches every form defined on a table from the currently signed-in D365
/// environment and saves each as its own file of this tool's declarative
/// YAML.
/// </summary>
public sealed class ExportFormCommand(IAuthenticationService authenticationService, IFormExportService formExportService)
    : AsyncCommand<ExportFormCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-t|--table <LOGICAL_NAME>")]
        [Description("Logical name of the table whose forms to export, e.g. account")]
        public required string Table { get; init; }

        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Scope the export to only the forms this solution customizes, instead of every form on the table.")]
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

            var forms = await AnsiConsole.Status().StartAsync($"Exporting forms for '{settings.Table}'...",
                async _ => await formExportService.ExportFormsAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Table, settings.Solution, cancellationToken));

            if (forms.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No forms found for '{settings.Table}'.[/]");
                return 0;
            }

            var outputDirectory = settings.Output ?? ".";
            Directory.CreateDirectory(outputDirectory);

            // "{name}.{asset type}.yml" — the same naming convention `table
            // export`/`view export` follow, so a folder of exported YAML
            // stays sortable and unambiguous across asset types.
            foreach (var form in forms)
            {
                var outputPath = Path.Combine(outputDirectory, $"{form.FileNameStem}.form.yml");
                await File.WriteAllTextAsync(outputPath, form.Yaml, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {outputPath}");
            }

            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
        catch (SolutionNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
        catch (InvalidDataException ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't parse the form metadata for '{settings.Table}':[/] {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
    }
}

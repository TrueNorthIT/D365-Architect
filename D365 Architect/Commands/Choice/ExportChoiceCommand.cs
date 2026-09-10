using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Choice;

/// <summary>
/// `d365architect choice export [--solution examplesolution] [--output choices.yml]`
/// Fetches every global choice in the currently signed-in D365 environment
/// (or, with `--solution`, just the ones that solution customizes) and
/// saves them all as one YAML file — see <see cref="IGlobalChoiceExportService"/>'s
/// own doc comment for why this is a list, not one file per choice.
/// </summary>
public sealed class ExportChoiceCommand(IAuthenticationService authenticationService, IGlobalChoiceExportService globalChoiceExportService)
    : AsyncCommand<ExportChoiceCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Scope the export to only the global choices this solution customizes, instead of every global choice in the environment.")]
        public string? Solution { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Path to write the YAML to. Defaults to choices.yml in the current directory.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var statusMessage = settings.Solution is null ? "Exporting every global choice..." : $"Exporting global choices for '{settings.Solution}'...";
            var yaml = await AnsiConsole.Status().StartAsync(statusMessage,
                async _ => await globalChoiceExportService.ExportGlobalChoicesAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Solution, cancellationToken));

            var outputPath = settings.Output ?? "choices.yml";
            await File.WriteAllTextAsync(outputPath, yaml, cancellationToken);

            AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {outputPath}");
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
            AnsiConsole.MarkupLine($"[red]Couldn't parse the global choice metadata:[/] {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
    }
}

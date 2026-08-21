using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Table;

/// <summary>
/// `d365architect table export --table account [--solution examplesolution] [--output account.table.yml]`
/// Fetches a table's live definition from the currently signed-in D365
/// environment and saves it as this tool's declarative YAML.
/// </summary>
public sealed class ExportTableCommand(IAuthenticationService authenticationService, ITableExportService tableExportService)
    : AsyncCommand<ExportTableCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-t|--table <LOGICAL_NAME>")]
        [Description("Logical name of the table to export, e.g. account")]
        public required string Table { get; init; }

        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Scope the export to only the columns this solution customizes, instead of the table's full definition.")]
        public string? Solution { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Path to write the YAML to. Defaults to <table>.table.yml in the current directory.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var yaml = await AnsiConsole.Status().StartAsync($"Exporting '{settings.Table}'...",
                async _ => await tableExportService.ExportTableAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Table, settings.Solution, cancellationToken));

            // "{name}.{asset type}.yml" — the naming convention every asset
            // export follows, so a folder of exported YAML stays sortable
            // and unambiguous once forms/views/etc. join tables.
            var outputPath = settings.Output ?? $"{settings.Table}.table.yml";
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
            AnsiConsole.MarkupLine($"[red]Couldn't parse the metadata for '{settings.Table}':[/] {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
    }
}

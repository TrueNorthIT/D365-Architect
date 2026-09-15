using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Solution;

/// <summary>
/// `d365architect solution export --solution examplesolution [--output .]`
/// Fetches every asset type this tool supports (tables, their views, their
/// forms, and global choices) for one solution from the currently
/// signed-in D365 environment, and writes them all as this tool's
/// declarative YAML under one folder — <c>&lt;output&gt;/&lt;solution&gt;/</c>,
/// with one sub-folder per table (<c>&lt;output&gt;/&lt;solution&gt;/&lt;entity&gt;/</c>)
/// holding that table's own <c>*.table.yml</c> plus its <c>*.view.yml</c>/
/// <c>*.form.yml</c> files, and a single <c>choices.yml</c> at the solution's
/// root for every global choice the solution customizes (omitted entirely
/// when it customizes none) — since a global choice isn't scoped to any one
/// table, the same reason <see cref="IGlobalChoiceExportService"/> is
/// already one file, not one per choice.
///
/// Which tables to export is discovered from the solution itself (its
/// Entity solution components), not passed in — unlike `table export`/`view
/// export`/`form export`, which always need `--table` because they have no
/// other way to know which one. This is otherwise a thin orchestration layer
/// over the same four export services those commands already use — see
/// <see cref="ISolutionExportService"/> for the actual composition.
/// </summary>
public sealed class ExportSolutionCommand(IAuthenticationService authenticationService, ISolutionExportService solutionExportService)
    : AsyncCommand<ExportSolutionCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of the solution to export.")]
        public required string Solution { get; init; }

        [CommandOption("-o|--output <DIRECTORY>")]
        [Description("Directory to create the solution's own folder under. Defaults to the current directory.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var result = await AnsiConsole.Status().StartAsync($"Exporting solution '{settings.Solution}'...",
                async _ => await solutionExportService.ExportSolutionAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Solution, cancellationToken));

            var solutionDirectory = Path.Combine(settings.Output ?? ".", settings.Solution);
            Directory.CreateDirectory(solutionDirectory);

            if (result.ChoicesYaml is not null)
            {
                var choicesPath = Path.Combine(solutionDirectory, "choices.yml");
                await File.WriteAllTextAsync(choicesPath, result.ChoicesYaml, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {choicesPath}");
            }

            var viewCount = 0;
            var formCount = 0;

            foreach (var entity in result.Entities)
            {
                var entityDirectory = Path.Combine(solutionDirectory, entity.EntityLogicalName);
                Directory.CreateDirectory(entityDirectory);

                var tablePath = Path.Combine(entityDirectory, $"{entity.EntityLogicalName}.table.yml");
                await File.WriteAllTextAsync(tablePath, entity.TableYaml, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {tablePath}");

                foreach (var view in entity.Views)
                {
                    var viewPath = Path.Combine(entityDirectory, $"{view.FileNameStem}.view.yml");
                    await File.WriteAllTextAsync(viewPath, view.Yaml, cancellationToken);
                    AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {viewPath}");
                    viewCount++;
                }

                foreach (var form in entity.Forms)
                {
                    var formPath = Path.Combine(entityDirectory, $"{form.FileNameStem}.form.yml");
                    await File.WriteAllTextAsync(formPath, form.Yaml, cancellationToken);
                    AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {formPath}");
                    formCount++;
                }
            }

            AnsiConsole.MarkupLine($"[green]Done.[/] Exported {result.Entities.Count} table(s), {viewCount} view(s), {formCount} form(s) to {solutionDirectory}");
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
            ErrorConsole.Print($"Couldn't parse the metadata for solution '{settings.Solution}': {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
    }
}

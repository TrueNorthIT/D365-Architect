using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Form;

/// <summary>
/// `d365architect form export --table account [--form-id &lt;GUID&gt;] [--solution examplesolution] [--output forms]`
/// Fetches one form from the currently signed-in D365 environment and saves
/// it as this tool's declarative YAML. When <see cref="Settings.FormId"/>
/// isn't given, prompts interactively (arrow keys + Enter, via Spectre.Console's
/// <c>SelectionPrompt</c>) to choose which of the
/// table's forms to export, using <see cref="IFormExportService.ListFormsAsync"/>
/// to populate that list cheaply (id/name/type only, not each form's full
/// FormXml) before committing to fetching and decomposing the one actually
/// chosen.
/// </summary>
public sealed class ExportFormCommand(IAuthenticationService authenticationService, IFormExportService formExportService)
    : AsyncCommand<ExportFormCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-t|--table <LOGICAL_NAME>")]
        [Description("Logical name of the table whose forms to choose from, e.g. account")]
        public required string Table { get; init; }

        [CommandOption("-f|--form-id <ID>")]
        [Description("Id of the form to export. Omit to choose interactively from a list.")]
        public Guid? FormId { get; init; }

        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Scope the interactive list (or the form id check) to only the forms this solution customizes.")]
        public string? Solution { get; init; }

        [CommandOption("-o|--output <DIRECTORY>")]
        [Description("Directory to write the exported YAML file into. Defaults to the current directory.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var formId = settings.FormId;
            if (formId is null)
            {
                formId = await PromptForFormIdAsync(auth.EnvironmentUrl, auth.AccessToken, settings, cancellationToken);
                if (formId is null)
                {
                    // No forms to choose from at all — already reported.
                    return 0;
                }
            }

            var forms = await AnsiConsole.Status().StartAsync($"Exporting form from '{settings.Table}'...",
                async _ => await formExportService.ExportFormsAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Table, settings.Solution, formId, cancellationToken));

            if (forms.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No form matching that id was found on '{settings.Table}'.[/]");
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
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
        catch (SolutionNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
        catch (InvalidDataException ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't parse the form metadata for '{settings.Table}':[/] {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
    }

    /// <returns>The chosen form's id, or null when there was nothing to choose from (already reported to the console).</returns>
    private async Task<Guid?> PromptForFormIdAsync(Uri environmentUrl, string accessToken, Settings settings, CancellationToken cancellationToken)
    {
        var summaries = await AnsiConsole.Status().StartAsync($"Looking up forms for '{settings.Table}'...",
            async _ => await formExportService.ListFormsAsync(environmentUrl, accessToken, settings.Table, settings.Solution, cancellationToken));

        if (summaries.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No forms found for '{settings.Table}'.[/]");
            return null;
        }

        var prompt = new SelectionPrompt<FormSummary>()
            .Title($"Select a form to export from [blue]{settings.Table}[/]:")
            .PageSize(10)
            .MoreChoicesText("[grey](Move up/down to reveal more forms)[/]")
            .UseConverter(form => form.Type is "Main" ? form.Name : $"{form.Name} [grey]({form.Type})[/]")
            .AddChoices(summaries);

        var chosen = AnsiConsole.Prompt(prompt);
        return chosen.FormId;
    }
}

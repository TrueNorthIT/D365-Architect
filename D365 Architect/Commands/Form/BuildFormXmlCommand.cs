using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Form;

/// <summary>
/// `d365architect form build-xml --input account-main-form.form.yml [--output account-main-form.xml]`
/// Rebuilds FormXML from one of this tool's curated `*.form.yml` files.
/// Needs sign-in: it retrieves the form's current, live FormXML first (by
/// table + name, matched against the currently signed-in environment) and
/// patches only the elements this tool manages onto that document, rather
/// than building a new `&lt;form&gt;` from scratch — see
/// <see cref="Services.Conversion.FormXmlWriter"/>'s own doc comment for
/// exactly what that does and doesn't preserve. When no form by that name
/// exists yet (a brand-new form this YAML describes but hasn't been created
/// in Dataverse), falls back to building fresh from just the YAML instead.
/// This is one building block toward a future `form import`, which would
/// actually write the result back — this command only ever reads.
/// </summary>
public sealed class BuildFormXmlCommand(IAuthenticationService authenticationService, IFormXmlBuildService formXmlBuildService)
    : AsyncCommand<BuildFormXmlCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <PATH>")]
        [Description("Path to the *.form.yml file to rebuild FormXML from.")]
        public required string Input { get; init; }

        [CommandOption("-o|--output <PATH>")]
        [Description("Path to write the rebuilt FormXML to. Defaults to <input>.xml.")]
        public string? Output { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.Input))
        {
            AnsiConsole.MarkupLine($"[red]'{settings.Input}' doesn't exist.[/]");
            return 1;
        }

        FormDefinition form;
        try
        {
            var yaml = await File.ReadAllTextAsync(settings.Input, cancellationToken);
            form = FormYamlDeserializer.FromYaml(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't parse '{settings.Input}' as a form:[/] {ex.Message}");
            return 1;
        }

        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var formXml = await AnsiConsole.Status().StartAsync($"Rebuilding FormXML for '{form.Name}'...",
                async _ => await formXmlBuildService.BuildFormXmlAsync(auth.EnvironmentUrl, auth.AccessToken, form, cancellationToken));

            var outputPath = settings.Output ?? Path.ChangeExtension(settings.Input, ".xml");
            await File.WriteAllTextAsync(outputPath, formXml, cancellationToken);

            AnsiConsole.MarkupLine($"[green]Wrote[/] {outputPath}");
            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
        catch (AmbiguousSystemFormException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
        catch (NotSupportedException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
    }
}

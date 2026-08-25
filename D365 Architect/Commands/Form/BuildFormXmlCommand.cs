using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
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
///
/// This is purely a local, read-only inspection/validation tool — it never
/// writes anything back into Dataverse, and <c>form import</c> doesn't run
/// this first or depend on it in any way; that command does its own
/// retrieve-and-patch independently. Point being: this exists so a human
/// can look at (and validate) what would get built, not as a required step
/// on the way to importing it.
///
/// Also validates the rebuilt FormXML against Microsoft's own official
/// FormXML XSD schema (see <see cref="Services.Conversion.FormXmlValidator"/>)
/// before writing it, and prints any violations as a warning rather than
/// refusing to write the file — a violation there isn't necessarily a bug
/// in this tool's own output; see that class's own doc comment for a
/// confirmed case where real, live Dataverse forms violate that same
/// schema too.
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
        var form = await FormYamlFileReader.TryReadAsync(settings.Input, cancellationToken);
        if (form is null)
        {
            return 1;
        }

        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var formXml = await AnsiConsole.Status().StartAsync($"Rebuilding FormXML for '{form.Name}'...",
                async _ => await formXmlBuildService.BuildFormXmlAsync(auth.EnvironmentUrl, auth.AccessToken, form, cancellationToken));

            FormXmlValidationConsole.PrintViolations(FormXmlValidator.Validate(formXml));

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
        catch (System.Xml.XmlException ex)
        {
            // Unlike a validation violation (reported above without
            // stopping), this means FormXmlWriter itself produced XML that
            // isn't even well-formed — a real bug in this tool, not a
            // known Dataverse quirk.
            AnsiConsole.MarkupLine($"[red]Rebuilt FormXML isn't well-formed XML — this is a bug in this tool, not the source YAML:[/] {ex.Message}");
            return 1;
        }
    }
}

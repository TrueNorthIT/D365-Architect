using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Spectre.Console;

namespace D365Architect.Commands.View;

/// <summary>Reads and parses a <c>*.view.yml</c> file, printing a consistent error and returning null rather than throwing.</summary>
internal static class ViewYamlFileReader
{
    public static async Task<ViewDefinition?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]'{path}' doesn't exist.[/]");
            return null;
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(path, cancellationToken);
            return ViewYamlDeserializer.FromYaml(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't parse '{path}' as a view:[/] {ex.Message}");
            return null;
        }
    }
}

using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Spectre.Console;

namespace D365Architect.Commands.Table;

/// <summary>Reads and parses a <c>*.table.yml</c> file, printing a consistent error and returning null rather than throwing.</summary>
internal static class EntityYamlFileReader
{
    public static async Task<EntityDefinition?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]'{path}' doesn't exist.[/]");
            return null;
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(path, cancellationToken);
            return EntityYamlDeserializer.FromYaml(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            AnsiConsole.MarkupLine($"[red]Couldn't parse '{path}' as a table:[/] {ex.Message}");
            return null;
        }
    }
}

using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Commands.Form;

/// <summary>
/// Reads and parses a <c>*.form.yml</c> file, printing a consistent error
/// and returning null rather than throwing — shared by
/// <see cref="BuildFormXmlCommand"/> and <see cref="ImportFormCommand"/>,
/// the two commands that start from one of these files rather than from a
/// live environment.
/// </summary>
internal static class FormYamlFileReader
{
    public static async Task<FormDefinition?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            ErrorConsole.Print($"'{path}' doesn't exist.");
            return null;
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(path, cancellationToken);
            return FormYamlDeserializer.FromYaml(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            ErrorConsole.Print($"Couldn't parse '{path}' as a form: {ex.Message}");
            return null;
        }
    }
}

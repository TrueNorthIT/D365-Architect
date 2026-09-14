namespace D365Architect.Commands;

/// <summary>
/// Reads and parses a curated `*.yml` file (form/table/view), printing a
/// consistent error and returning null rather than throwing. The
/// deserializer and the "as a ___" label in the parse-error message are the
/// only things that actually differ between a `*.form.yml`/`*.table.yml`/
/// `*.view.yml` read — passed in here rather than this being three
/// separate, near-identical reader classes.
/// </summary>
internal static class YamlFileReader
{
    public static async Task<T?> TryReadAsync<T>(string path, string kind, Func<string, T> deserialize, CancellationToken cancellationToken)
        where T : class
    {
        if (!File.Exists(path))
        {
            ErrorConsole.Print($"'{path}' doesn't exist.");
            return null;
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(path, cancellationToken);
            return deserialize(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            ErrorConsole.Print($"Couldn't parse '{path}' as a {kind}: {ex.Message}");
            return null;
        }
    }
}

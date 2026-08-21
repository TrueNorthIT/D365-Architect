namespace D365Architect.Services.Conversion;

/// <summary>
/// Entry point for turning unpacked Dynamics solution component XML files into
/// this tool's declarative YAML. Dispatches to whichever registered
/// <see cref="IComponentXmlConverter"/> recognises the file.
/// </summary>
public interface IXmlToYamlConverterService
{
    /// <summary>
    /// Converts a single unpacked solution component file (e.g.
    /// <c>Entities/account/Entity.xml</c>) to its curated YAML representation.
    /// </summary>
    /// <exception cref="NotSupportedException">No registered converter recognises this component type yet.</exception>
    /// <exception cref="InvalidDataException">The file's XML doesn't match the shape expected for its component type.</exception>
    Task<string> ConvertFileAsync(string filePath, CancellationToken cancellationToken);
}

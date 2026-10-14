namespace D365Architect.Services.Conversion;

/// <summary>
/// Converts one kind of unpacked Dynamics solution component (e.g. an
/// <c>Entities/{name}/Entity.xml</c> produced by `pac solution unpack`) from
/// its raw XML into this tool's curated YAML representation.
///
/// Register one implementation per component type with DI; <see cref="IXmlToYamlConverterService"/>
/// picks whichever one recognises the file being converted. Add support for
/// FormXml, SavedQuery (views), Ribbon, etc. by dropping in a new implementation
/// here rather than growing an existing one.
/// </summary>
public interface IComponentXmlConverter
{
    /// <summary>True if this converter understands the component at <paramref name="filePath"/>.</summary>
    bool CanConvert(string filePath);

    /// <summary>Converts the component's raw XML content into curated YAML.</summary>
    /// <exception cref="InvalidDataException">The XML doesn't match the shape this converter expects.</exception>
    string ConvertToYaml(string xml);
}

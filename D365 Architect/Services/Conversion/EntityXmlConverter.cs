namespace D365Architect.Services.Conversion;

/// <summary>
/// Adapts <see cref="EntityXmlDefinitionReader"/> to the file-based
/// <see cref="IComponentXmlConverter"/> pipeline used for unpacked solution
/// components. Parsing itself lives on the reader (the XML strategy for
/// <see cref="IEntityDefinitionReader"/>) so it can be reused wherever an
/// Entity.xml shows up, not just here.
/// </summary>
public sealed class EntityXmlConverter(EntityXmlDefinitionReader reader) : IComponentXmlConverter
{
    public bool CanConvert(string filePath) =>
        string.Equals(Path.GetFileName(filePath), "Entity.xml", StringComparison.OrdinalIgnoreCase);

    public string ConvertToYaml(string xml) => EntityYamlSerializer.ToYaml(reader.Read(xml));
}

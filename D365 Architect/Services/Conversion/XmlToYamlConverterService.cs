namespace D365Architect.Services.Conversion;

public sealed class XmlToYamlConverterService(IEnumerable<IComponentXmlConverter> converters) : IXmlToYamlConverterService
{
    public async Task<string> ConvertFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var converter = converters.FirstOrDefault(c => c.CanConvert(filePath))
            ?? throw new NotSupportedException(
                $"No XML-to-YAML converter is registered for '{Path.GetFileName(filePath)}' yet.");

        var xml = await File.ReadAllTextAsync(filePath, cancellationToken);
        return converter.ConvertToYaml(xml);
    }
}

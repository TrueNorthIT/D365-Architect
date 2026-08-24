using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to read a curated <see cref="FormDefinition"/>
/// back out of YAML — the reverse of <see cref="FormYamlSerializer"/>, and
/// the first step in reconstructing FormXML (see <see cref="FormXmlWriter"/>).
/// </summary>
internal static class FormYamlDeserializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ReadOnlyListYamlTypeConverter())
        .Build();

    /// <exception cref="YamlDotNet.Core.YamlException">The YAML doesn't match this tool's curated form shape.</exception>
    public static FormDefinition FromYaml(string yaml) => Deserializer.Deserialize<FormDefinition>(yaml);
}

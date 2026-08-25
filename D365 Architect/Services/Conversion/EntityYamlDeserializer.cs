using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to read a curated <see cref="EntityDefinition"/>
/// back out of YAML — the reverse of <see cref="EntityYamlSerializer"/>, and
/// the first step in <c>table import</c>.
/// </summary>
internal static class EntityYamlDeserializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ReadOnlyListYamlTypeConverter())
        .Build();

    /// <exception cref="YamlDotNet.Core.YamlException">The YAML doesn't match this tool's curated table shape.</exception>
    public static EntityDefinition FromYaml(string yaml) => Deserializer.Deserialize<EntityDefinition>(yaml);
}

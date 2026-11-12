using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>Reads a <c>*.security.yml</c> back into a <see cref="TableSecurityDefinition"/>.</summary>
internal static class TableSecurityYamlDeserializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ReadOnlyListYamlTypeConverter())
        .Build();

    /// <exception cref="YamlDotNet.Core.YamlException">The YAML doesn't match the table security shape, e.g. an access level that isn't one of the five.</exception>
    public static TableSecurityDefinition FromYaml(string yaml) => Deserializer.Deserialize<TableSecurityDefinition>(yaml);
}

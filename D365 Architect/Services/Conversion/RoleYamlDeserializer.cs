using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>Reads a <c>*.role.yml</c> back into a <see cref="RoleDefinition"/>.</summary>
internal static class RoleYamlDeserializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ReadOnlyDictionaryYamlTypeConverter())
        .Build();

    /// <exception cref="YamlDotNet.Core.YamlException">The YAML doesn't match the role shape, e.g. an access level that isn't one of the five.</exception>
    public static RoleDefinition FromYaml(string yaml) => Deserializer.Deserialize<RoleDefinition>(yaml);
}

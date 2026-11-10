using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>Renders a <see cref="RoleDefinition"/> as the <c>*.role.yml</c> text. Only privileges that are set are written.</summary>
internal static class RoleYamlSerializer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    public static string ToYaml(RoleDefinition definition) => Serializer.Serialize(definition);
}

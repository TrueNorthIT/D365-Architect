using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>Renders a <see cref="TableSecurityDefinition"/> as the <c>*.security.yml</c> text. All eight privileges are always written for every role (<c>None</c> included).</summary>
internal static class TableSecurityYamlSerializer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static string ToYaml(TableSecurityDefinition definition) => Serializer.Serialize(definition);
}

using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to render a curated <see cref="ViewDefinition"/>
/// as YAML — the <see cref="EntityYamlSerializer"/> counterpart for views.
/// </summary>
internal static class ViewYamlSerializer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static string ToYaml(ViewDefinition definition) => Serializer.Serialize(definition);
}

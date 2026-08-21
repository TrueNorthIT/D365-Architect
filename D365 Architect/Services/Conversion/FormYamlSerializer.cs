using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to render a curated <see cref="FormDefinition"/>
/// as YAML — the <see cref="EntityYamlSerializer"/>/<see cref="ViewYamlSerializer"/> counterpart for forms.
/// </summary>
internal static class FormYamlSerializer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static string ToYaml(FormDefinition definition) => Serializer.Serialize(definition);
}

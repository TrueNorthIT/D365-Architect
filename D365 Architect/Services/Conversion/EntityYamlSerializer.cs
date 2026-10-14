using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to render a curated <see cref="EntityDefinition"/>
/// as YAML. Shared by every <see cref="IEntityDefinitionReader"/> strategy's
/// consumer, so the YAML shape stays identical no matter which strategy
/// produced the model.
/// </summary>
internal static class EntityYamlSerializer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static string ToYaml(EntityDefinition definition) => Serializer.Serialize(definition);
}

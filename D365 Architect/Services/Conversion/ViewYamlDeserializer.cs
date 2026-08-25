using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to read a curated <see cref="ViewDefinition"/>
/// back out of YAML — the reverse of <see cref="ViewYamlSerializer"/>, and
/// the first step in <c>view import</c>.
/// </summary>
internal static class ViewYamlDeserializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <exception cref="YamlDotNet.Core.YamlException">The YAML doesn't match this tool's curated view shape.</exception>
    public static ViewDefinition FromYaml(string yaml) => Deserializer.Deserialize<ViewDefinition>(yaml);
}

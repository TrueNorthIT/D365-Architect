using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to read curated <see cref="GlobalChoiceDefinition"/>s
/// back out of YAML — the reverse of <see cref="GlobalChoiceYamlSerializer"/>,
/// and the first step in <c>choice import</c>. Deserializes a top-level YAML
/// sequence, matching what <c>choice export</c> writes.
/// </summary>
internal static class GlobalChoiceYamlDeserializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ReadOnlyListYamlTypeConverter())
        .Build();

    /// <exception cref="YamlDotNet.Core.YamlException">The YAML doesn't match this tool's curated global choice list shape.</exception>
    public static IReadOnlyList<GlobalChoiceDefinition> FromYaml(string yaml) => Deserializer.Deserialize<List<GlobalChoiceDefinition>>(yaml);
}

using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The one place that knows how to render curated <see cref="GlobalChoiceDefinition"/>s
/// as YAML — the <c>choice export</c>/<c>choice import</c> counterpart to
/// <see cref="EntityYamlSerializer"/>. A `*.choice.yml` file is a plain YAML
/// sequence at the top level (never a single choice on its own) — see
/// <see cref="IGlobalChoiceExportService"/>'s own doc comment for why one
/// file per choice was dropped in favor of this.
/// </summary>
internal static class GlobalChoiceYamlSerializer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static string ToYaml(IReadOnlyList<GlobalChoiceDefinition> choices) => Serializer.Serialize(choices);
}

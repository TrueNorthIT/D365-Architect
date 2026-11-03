using D365Architect.Services.Schema;
using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// One entry of a <see cref="ViewDefinition.Columns"/> list. In YAML it is
/// either a plain string (<c>- createdon</c>, default width) or a mapping
/// (<c>- name: bogdan_name</c> / <c>width: 300</c>); see
/// <see cref="ViewColumnYamlTypeConverter"/>, which reads both and writes the
/// plain string whenever there is no width.
/// </summary>
[SchemaPlainStringShorthand]
public sealed class ViewColumn
{
    /// <summary>The column's logical name, e.g. "bogdan_name".</summary>
    [YamlMember(Order = 0)]
    public required string Name { get; init; }

    /// <summary>Width in pixels. Omit for the default of 150.</summary>
    [YamlMember(Order = 1)]
    public int? Width { get; init; }
}

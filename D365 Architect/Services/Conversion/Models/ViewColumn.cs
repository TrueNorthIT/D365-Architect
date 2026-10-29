using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// One entry of a <see cref="ViewDefinition.Columns"/> list. In YAML it is
/// either a plain string (<c>- createdon</c>, default width) or a mapping
/// (<c>- name: bogdan_name</c> / <c>width: 300</c>). Pinned by
/// <c>ViewColumnsYamlTests</c>; the YAML converter that reads both shapes is the
/// developer's to write (test-first stub: the model only).
/// </summary>
public sealed class ViewColumn
{
    /// <summary>The column's logical name, e.g. "bogdan_name".</summary>
    [YamlMember(Order = 0)]
    public required string Name { get; init; }

    /// <summary>Width in pixels. Null means "use the default" (150, see <c>ViewImportServiceColumnsTests</c>).</summary>
    [YamlMember(Order = 1)]
    public int? Width { get; init; }
}

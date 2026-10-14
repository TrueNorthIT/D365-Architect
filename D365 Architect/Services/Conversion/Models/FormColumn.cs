using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// One side-by-side layout column within a <see cref="FormTab"/> — FormXML's
/// `&lt;column&gt;`. A tab with a single, full-width column is still
/// represented as one <see cref="FormColumn"/> here (rather than omitted),
/// so a tab's column count is always visible directly, not implied by
/// whether this property happens to be present.
/// </summary>
public sealed class FormColumn
{
    /// <summary>This column's relative width, e.g. "50%", "33%", "100%" — kept as Dataverse states it, not normalised.</summary>
    [YamlMember(Order = 0)]
    public string? Width { get; init; }

    /// <summary>The sections stacked in this column, top to bottom.</summary>
    [YamlMember(Order = 1)]
    public IReadOnlyList<FormSection> Sections { get; init; } = [];
}

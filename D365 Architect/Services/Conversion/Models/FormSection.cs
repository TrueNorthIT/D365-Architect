using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// A single section within a <see cref="FormColumn"/> — FormXML's
/// `&lt;section&gt;`.
/// </summary>
public sealed class FormSection
{
    /// <summary>The section's internal name, e.g. "tab_1_column_1_section_1".</summary>
    [YamlMember(Order = 0)]
    public string? Name { get; init; }

    [YamlMember(Order = 1)]
    public string? Label { get; init; }

    /// <summary>
    /// How many side-by-side sub-columns this section itself renders its
    /// rows into — a section can lay its own cells out into more than one
    /// column independently of <see cref="FormColumn"/> (a tab's own
    /// columns). <see cref="Controls"/> stays a flat, row-major list either
    /// way (row 1's cells left-to-right, then row 2's, ...), so this is the
    /// piece of information that says how to regroup them back into a
    /// grid — the cell/row structure itself isn't otherwise represented.
    /// Only present when greater than 1 — a single column is the common
    /// case; applying this file back won't change a section's own column
    /// count.
    /// </summary>
    [YamlMember(Order = 2)]
    public int? Columns { get; init; }

    [YamlMember(Order = 3)]
    public IReadOnlyList<FormControl> Controls { get; init; } = [];
}

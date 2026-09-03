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

    /// <summary>The section's display label.</summary>
    [YamlMember(Order = 1)]
    public string? Label { get; init; }

    /// <summary>
    /// This label's text in every language besides the one shown as
    /// <see cref="Label"/>, keyed by Dataverse's own languagecode (e.g.
    /// 1036 for French). Absent on a single-language tenant (the
    /// overwhelming common case) or when this section has only its own.
    /// </summary>
    /// <remarks>
    /// Previously every translation but the primary one was silently
    /// discarded on export — confirmed as a real gap, not a stripped
    /// default: this is genuine maker-authored text on a multi-language
    /// tenant, lost permanently on every round-trip until this was added.
    /// </remarks>
    [YamlMember(Order = 2)]
    public IReadOnlyDictionary<int, string>? Translations { get; init; }

    /// <summary>
    /// How many side-by-side sub-columns this section lays its own fields
    /// into — independent of the tab's own columns. Only present when
    /// greater than 1 — a single column is the common case; applying this
    /// file back won't change a section's own column count.
    /// </summary>
    /// <remarks>
    /// <see cref="Controls"/> stays a flat, row-major list either way (row
    /// 1's cells left-to-right, then row 2's, ...); this is what says how
    /// to regroup them back into a grid.
    /// </remarks>
    [YamlMember(Order = 3)]
    public int? Columns { get; init; }

    /// <summary>The section's fields, in reading order (left to right, top to bottom).</summary>
    [YamlMember(Order = 4)]
    public IReadOnlyList<FormControl> Controls { get; init; } = [];

    /// <summary>
    /// Only present when false — a deliberately hidden section, distinct
    /// from one simply not on the form at all. Omit to leave this section
    /// visible; applying this file back won't hide it.
    /// </summary>
    [YamlMember(Order = 5)]
    public bool? Visible { get; init; }

    /// <summary>
    /// Only present when false — this section's own header label is
    /// deliberately hidden. Omit to leave this section's label shown;
    /// applying this file back won't hide it.
    /// </summary>
    [YamlMember(Order = 6)]
    public bool? ShowLabel { get; init; }

    /// <summary>
    /// Whether this section shows on the phone-optimized layout —
    /// FormXML's `availableforphone` attribute, shown exactly as stated
    /// rather than defaulted/stripped: which direction is the common case
    /// hasn't been confirmed against real tenant samples, so nothing is
    /// assumed either way. Absent when this section's FormXML doesn't set
    /// it at all.
    /// </summary>
    [YamlMember(Order = 7)]
    public bool? AvailableOnPhone { get; init; }
}

using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// A single tab on a <see cref="FormDefinition"/> — FormXML's `&lt;tab&gt;`.
/// A tab lays its sections out into one or more side-by-side
/// `&lt;column&gt;`s — real structure, not a cosmetic detail: two sections in
/// different columns of the same tab render next to each other, not one
/// above the other, so which column a section is in is kept as
/// <see cref="FormColumn"/> rather than flattened away.
/// </summary>
public sealed class FormTab
{
    /// <summary>The tab's internal name, e.g. "tab_1".</summary>
    [YamlMember(Order = 0)]
    public string? Name { get; init; }

    /// <summary>The tab's display label.</summary>
    [YamlMember(Order = 1)]
    public string? Label { get; init; }

    /// <summary>
    /// This label's text in every language besides the one shown as
    /// <see cref="Label"/>, keyed by Dataverse's own languagecode (e.g.
    /// 1036 for French). Absent on a single-language tenant (the
    /// overwhelming common case) or when this tab has only its own.
    /// </summary>
    /// <remarks>
    /// Previously every translation but the primary one was silently
    /// discarded on export — confirmed as a real gap, not a stripped
    /// default: this is genuine maker-authored text on a multi-language
    /// tenant, lost permanently on every round-trip until this was added.
    /// </remarks>
    [YamlMember(Order = 2)]
    public IReadOnlyDictionary<int, string>? Translations { get; init; }

    /// <summary>The tab's side-by-side columns, left to right.</summary>
    [YamlMember(Order = 3)]
    public IReadOnlyList<FormColumn> Columns { get; init; } = [];

    /// <summary>
    /// Only present when false — a deliberately hidden tab, distinct from
    /// one simply not on the form at all. Omit to leave this tab visible;
    /// applying this file back won't hide it.
    /// </summary>
    [YamlMember(Order = 4)]
    public bool? Visible { get; init; }

    /// <summary>
    /// Whether an end user can collapse/expand this tab — FormXML's
    /// `collapsible` attribute, shown exactly as stated rather than
    /// defaulted/stripped: which direction (true or false) is the common
    /// case hasn't been confirmed against real tenant samples, so nothing
    /// is assumed either way. Absent when this tab's FormXML doesn't set
    /// it at all.
    /// </summary>
    [YamlMember(Order = 5)]
    public bool? Collapsible { get; init; }

    /// <summary>
    /// Whether this tab shows on the phone-optimized layout — FormXML's
    /// `availableforphone` attribute, shown exactly as stated rather than
    /// defaulted/stripped: which direction is the common case hasn't been
    /// confirmed against real tenant samples, so nothing is assumed either
    /// way. Absent when this tab's FormXML doesn't set it at all.
    /// </summary>
    [YamlMember(Order = 6)]
    public bool? AvailableOnPhone { get; init; }
}

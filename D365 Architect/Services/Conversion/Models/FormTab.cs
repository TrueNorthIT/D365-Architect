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

    /// <summary>The tab's side-by-side columns, left to right.</summary>
    [YamlMember(Order = 2)]
    public IReadOnlyList<FormColumn> Columns { get; init; } = [];
}

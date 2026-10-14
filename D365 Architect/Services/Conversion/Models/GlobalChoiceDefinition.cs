using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated YAML shape for a Dataverse global choice
/// (<c>GlobalOptionSetDefinitions</c>) — the shared, cross-table option set
/// a Picklist/MultiSelectPicklist column can reference by name (see
/// <see cref="AttributeDefinition.GlobalOptionSetName"/>). Sourced live from
/// the Dataverse Web API — see <see cref="GlobalChoiceJsonReader"/>.
///
/// Every global choice this tool creates is <c>OptionSetType: "Picklist"</c>
/// — confirmed against Microsoft's own documented <c>CreateOptionSet</c>
/// example — so that property isn't exposed here at all; a Picklist-typed
/// global choice already works for both single- and multi-select columns
/// (this tool's own Picklist/MultiSelectPicklist create bodies bind to one
/// exactly the same way — see `docs/yaml-conventions.md`).
///
/// Absent optional properties below mean "left at Dataverse's own default",
/// not "unknown" — see <see cref="DefaultValueConventions"/>, same
/// convention as <see cref="EntityDefinition"/>/<see cref="AttributeDefinition"/>.
/// </summary>
public sealed class GlobalChoiceDefinition
{
    /// <summary>
    /// The choice's unique name, e.g. "new_colors" — its stable identity
    /// (there's no separate internal id in this YAML, the way a form needs
    /// <c>FormId</c>): Dataverse's own <c>Name</c> never changes after
    /// creation, so it's always safe to look up by.
    /// </summary>
    [YamlMember(Order = 0)]
    public required string Name { get; init; }

    /// <summary>The choice's display name.</summary>
    [YamlMember(Order = 1)]
    public string? DisplayName { get; init; }

    /// <summary>The choice's description.</summary>
    [YamlMember(Order = 2)]
    public string? Description { get; init; }

    /// <summary>
    /// The choice's own values. Required to create a brand-new choice — see
    /// <see cref="AttributeOptionDefinition"/> for why a <c>value</c> is
    /// always explicit, never invented. Null on an update-only YAML means
    /// "don't touch the options", same as everywhere else in this tool.
    /// </summary>
    [YamlMember(Order = 3)]
    public IReadOnlyList<AttributeOptionDefinition>? Options { get; init; }
}

using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// One choice in a Picklist/MultiSelectPicklist's local <c>OptionSet</c>, or
/// one status reason in a Status column's <c>OptionSet</c> — see
/// <see cref="AttributeDefinition.Options"/>.
///
/// <see cref="Value"/> is always explicit, never invented by this tool: on
/// create, Dataverse's own Web API requires every option's integer value up
/// front (unlike a single later <c>InsertOptionValue</c> call, which can let
/// Dataverse assign one) — see
/// https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-column-definitions-using-web-api#create-a-choice-column.
/// Guessing a value here (e.g. picking an arbitrary base like Microsoft's own
/// example <c>727000000</c>) risks colliding with the organization's actual
/// publisher-assigned option value prefix, so this tool always requires one
/// in the local YAML rather than inventing it — the same policy already
/// applied to a column's own <see cref="AttributeDefinition.SchemaName"/>.
///
/// **Status is the one deliberate exception**: <c>InsertStatusValue</c>'s
/// own documented request body has no <c>Value</c> parameter at all —
/// Dataverse always assigns a status reason's value itself, confirmed
/// against Microsoft's own docs, so a still-required <see cref="Value"/>
/// here is never sent for a new Status option; it's an unused placeholder
/// until the next export fills in the real one (see
/// `docs/yaml-conventions.md`'s "State and Status" section for exactly how
/// a Status option is matched without relying on it).
/// </summary>
public sealed class AttributeOptionDefinition
{
    /// <summary>The option's integer value, e.g. 727000000. Ignored for a Status option being newly inserted — see this class's own doc comment.</summary>
    [YamlMember(Order = 0)]
    public required int Value { get; init; }

    /// <summary>The option's display label.</summary>
    [YamlMember(Order = 1)]
    public required string Label { get; init; }

    /// <summary>
    /// Status only: which State (<c>0</c> = Active, <c>1</c> = Inactive, by
    /// Dataverse's own convention — a table can define more) this status
    /// reason belongs to. Required to insert a brand-new Status option —
    /// Dataverse's <c>InsertStatusValue</c> action needs it and this tool
    /// never guesses it — but not otherwise meaningful (Picklist/
    /// MultiSelectPicklist/State options don't have one; a State option's
    /// own <see cref="Value"/> already <em>is</em> its state).
    /// </summary>
    [YamlMember(Order = 2)]
    public int? State { get; init; }
}

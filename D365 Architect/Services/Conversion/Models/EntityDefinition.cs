using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated, hand-designed YAML shape for a Dynamics table. Sourced either
/// from an unpacked solution's <c>Entities/{name}/Entity.xml</c> or live from
/// the Dataverse Web API (see <see cref="IEntityDefinitionReader"/>).
///
/// Property choice is driven by what Dataverse's own create/update table API
/// actually needs (see
/// https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api):
/// <see cref="SchemaName"/>, <see cref="OwnershipType"/>, <see cref="IsActivity"/>,
/// <see cref="HasActivities"/> and <see cref="HasNotes"/> are all required to
/// create a table, so they're captured here even though they rarely change
/// once a table exists — a future "apply" needs them to tell a real change
/// from a table that was simply never fully described.
///
/// Deliberately excluded: PrimaryNameAttribute. It's read-only/computed —
/// Dataverse derives it from whichever attribute has IsPrimaryName set — so
/// it's never something this YAML could actually drive; it would only ever
/// restate what <see cref="AttributeDefinition"/> already says elsewhere.
///
/// Absent optional properties below mean "left at Dataverse's default", not
/// "unknown" — see <see cref="DefaultValueConventions"/>.
/// </summary>
public sealed class EntityDefinition
{
    /// <summary>The table's logical name, e.g. "account" — written as the top-level "entity" key.</summary>
    [YamlMember(Alias = "entity", Order = 0)]
    public required string LogicalName { get; init; }

    /// <summary>
    /// The customization-prefixed schema name, e.g. "new_BankAccount". Fixed
    /// at creation time — required when creating a table, immutable after.
    /// </summary>
    [YamlMember(Order = 1)]
    public string? SchemaName { get; init; }

    /// <summary>The table's singular display name, e.g. "Account".</summary>
    [YamlMember(Order = 2)]
    public string? DisplayName { get; init; }

    /// <summary>The table's plural display name, e.g. "Accounts".</summary>
    [YamlMember(Order = 3)]
    public string? PluralDisplayName { get; init; }

    /// <summary>The table's description.</summary>
    [YamlMember(Order = 4)]
    public string? Description { get; init; }

    /// <summary>"UserOwned", "OrganizationOwned", etc. Required to create a table; can't be changed afterwards.</summary>
    [YamlMember(Order = 5)]
    public string? OwnershipType { get; init; }

    /// <summary>Only present when true — false is the platform default for a table. Omit to leave it as an ordinary table; applying this file back won't make it an activity type.</summary>
    [YamlMember(Order = 6)]
    public bool? IsActivity { get; init; }

    /// <summary>Only present when true — false is the platform default for a table. Omit to leave activities off for this table; applying this file back won't turn them on.</summary>
    [YamlMember(Order = 7)]
    public bool? HasActivities { get; init; }

    /// <summary>Only present when true — false is the platform default for a table. Omit to leave notes off for this table; applying this file back won't turn them on.</summary>
    [YamlMember(Order = 8)]
    public bool? HasNotes { get; init; }

    /// <summary>The table's columns.</summary>
    [YamlMember(Order = 9)]
    public IReadOnlyList<AttributeDefinition> Attributes { get; init; } = [];

    /// <summary>
    /// A copy with <see cref="Attributes"/> replaced — everything else
    /// carried over unchanged. Exists for <c>table import</c>'s diff display:
    /// re-exporting a table right now returns columns in Dataverse's own
    /// order (e.g. creation order), which is almost never the order a human
    /// wrote them in the local YAML — diffing the two as plain text then
    /// misreports pure reordering as whole columns being removed and
    /// re-added (a plain line-level LCS diff can't match a moved block that
    /// crosses another moved block without breaking one of them up). Calling
    /// this with the live columns re-sorted to the local file's own column
    /// order before diffing means identical columns line up positionally
    /// regardless of which order Dataverse happens to return them in, so
    /// only genuine differences show.
    /// </summary>
    public EntityDefinition WithAttributes(IReadOnlyList<AttributeDefinition> attributes) => new()
    {
        LogicalName = LogicalName,
        SchemaName = SchemaName,
        DisplayName = DisplayName,
        PluralDisplayName = PluralDisplayName,
        Description = Description,
        OwnershipType = OwnershipType,
        IsActivity = IsActivity,
        HasActivities = HasActivities,
        HasNotes = HasNotes,
        Attributes = attributes,
    };
}

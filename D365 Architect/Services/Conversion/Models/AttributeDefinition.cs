using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated YAML shape for a single column on an <see cref="EntityDefinition"/>.
///
/// Property choice is driven by what Dataverse's own create/update column API
/// actually needs per column type (see
/// https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-column-definitions-using-web-api):
/// <see cref="SchemaName"/> and <see cref="RequiredLevel"/> are needed for
/// every type; <see cref="MaxLength"/>/<see cref="Format"/> for strings and
/// memos; <see cref="Precision"/>/<see cref="PrecisionSource"/>/<see cref="MinValue"/>/<see cref="MaxValue"/>
/// for money/decimal/integer; <see cref="Targets"/> for lookups;
/// <see cref="Options"/>/<see cref="GlobalOptionSetName"/> for
/// Picklist/MultiSelectPicklist/Status;
/// <see cref="DefaultValue"/>/<see cref="TrueOptionLabel"/>/<see cref="FalseOptionLabel"/>
/// for Boolean; <see cref="RelationshipSchemaName"/> for a brand-new Lookup.
///
/// Unlike everything else here, a Picklist/Boolean/MultiSelectPicklist/Status
/// column's choice values can't be read via the bulk query this reader
/// otherwise relies on — Dataverse only returns them from a separate,
/// per-attribute, type-cast request (<c>EntityJsonDefinitionReader.Read</c>'s
/// own doc comment explains how that request result gets merged in here).
///
/// Absent optional properties below mean "left at Dataverse's default", not
/// "unknown" — see <see cref="DefaultValueConventions"/>.
/// </summary>
public sealed class AttributeDefinition
{
    /// <summary>The column's logical name, e.g. "name".</summary>
    [YamlMember(Order = 0)]
    public required string Name { get; init; }

    /// <summary>
    /// The customization-prefixed schema name, e.g. "new_BankName". Required
    /// to create a column; immutable after.
    /// </summary>
    [YamlMember(Order = 1)]
    public string? SchemaName { get; init; }

    /// <summary>The column's Dynamics type, e.g. "nvarchar", "primarykey", "lookup".</summary>
    [YamlMember(Order = 2)]
    public required string Type { get; init; }

    /// <summary>The column's display name.</summary>
    [YamlMember(Order = 3)]
    public string? DisplayName { get; init; }

    /// <summary>The column's description.</summary>
    [YamlMember(Order = 4)]
    public string? Description { get; init; }

    /// <summary>
    /// The underlying SQL column name, when it differs from the logical
    /// name in more than casing — only present when exported from a legacy
    /// unpacked solution's <c>Entity.xml</c>; live exports never set this,
    /// since Dataverse manages its own storage rather than mapping columns
    /// onto SQL Server directly.
    /// </summary>
    [YamlMember(Order = 5)]
    public string? PhysicalName { get; init; }

    /// <summary>
    /// "recommended", "applicationrequired" or "systemrequired". Only present
    /// when set to something other than "none" — that's Dataverse's default
    /// for every column type. Omit for an optional column; applying this
    /// file back won't add a requirement level.
    /// </summary>
    [YamlMember(Order = 6)]
    public string? RequiredLevel { get; init; }

    /// <summary>String/Memo character limit.</summary>
    [YamlMember(Order = 7)]
    public int? MaxLength { get; init; }

    /// <summary>Decimal places. For Money, only meaningful when precisionSource is 2 (this column's own).</summary>
    [YamlMember(Order = 8)]
    public int? Precision { get; init; }

    /// <summary>Money only: 0 = currency-specific, 1 = organization setting, 2 = this column's own precision.</summary>
    [YamlMember(Order = 9)]
    public int? PrecisionSource { get; init; }

    /// <summary>Money/Decimal/Integer lower bound.</summary>
    [YamlMember(Order = 10)]
    public double? MinValue { get; init; }

    /// <summary>Money/Decimal/Integer upper bound.</summary>
    [YamlMember(Order = 11)]
    public double? MaxValue { get; init; }

    /// <summary>Type-specific format, e.g. "Email"/"Text" (String), "DateOnly"/"DateAndTime" (DateTime), "TextArea" (Memo).</summary>
    [YamlMember(Order = 12)]
    public string? Format { get; init; }

    /// <summary>Lookup/Customer/Owner: logical names of the table(s) this column can point to.</summary>
    [YamlMember(Order = 13)]
    public IReadOnlyList<string>? Targets { get; init; }

    /// <summary>Only present when true — false (a standard, non-custom field) is by far the common case. Purely informational: applying this file back never changes it either way — whether a column is custom is determined by how it was created, not something re-set on update.</summary>
    [YamlMember(Order = 14)]
    public bool? IsCustomField { get; init; }

    /// <summary>Whether this column can be used in Advanced Find.</summary>
    [YamlMember(Order = 15)]
    public bool? ValidForAdvancedFind { get; init; }

    /// <summary>
    /// A Picklist/MultiSelectPicklist/State/Status column's own choice
    /// values, when it's backed by a *local* option set — null instead when
    /// it's backed by a global one (see <see cref="GlobalOptionSetName"/>;
    /// confirmed live that State/Status in particular normally are). Never
    /// set together with <see cref="GlobalOptionSetName"/> — a column uses
    /// one or the other, never both. See <see cref="AttributeOptionDefinition"/>
    /// for why <see cref="AttributeOptionDefinition.Value"/> is always
    /// explicit.
    /// </summary>
    [YamlMember(Order = 16)]
    public IReadOnlyList<AttributeOptionDefinition>? Options { get; init; }

    /// <summary>
    /// The shared <c>GlobalOptionSetDefinitions</c> choice this column uses
    /// instead of its own local <see cref="Options"/> — applies to
    /// Picklist/MultiSelectPicklist, and, confirmed live, State/Status too
    /// (every <c>statecode</c>/<c>statuscode</c> checked on a real tenant,
    /// standard and custom tables alike, turned out to be backed by a global
    /// <c>{entity}_statecode</c>/<c>{entity}_statuscode</c> choice, not a
    /// local one). This tool only ever *references* an existing global
    /// choice by name — it never creates, edits, or deletes one, since
    /// that's a shared, cross-table resource with a much wider blast radius
    /// than a single column; this also means State/Status option-renaming
    /// is inert whenever this is set (see `docs/yaml-conventions.md`).
    /// </summary>
    [YamlMember(Order = 17)]
    public string? GlobalOptionSetName { get; init; }

    /// <summary>Boolean only: the value a new record gets for this column when nothing else is set.</summary>
    [YamlMember(Order = 18)]
    public bool? DefaultValue { get; init; }

    /// <summary>Boolean only: the label shown for "true". Omitted when it's Dataverse's own default, "True".</summary>
    [YamlMember(Order = 19)]
    public string? TrueOptionLabel { get; init; }

    /// <summary>Boolean only: the label shown for "false". Omitted when it's Dataverse's own default, "False".</summary>
    [YamlMember(Order = 20)]
    public string? FalseOptionLabel { get; init; }

    /// <summary>
    /// Lookup only, required to create a brand-new Lookup column: the
    /// schema name of the one-to-many <em>relationship</em> that owns this
    /// lookup — a separate name from the column's own <see cref="SchemaName"/>,
    /// never inferred. Not used for Customer (its two relationship names are
    /// derived from <see cref="SchemaName"/> instead — see
    /// `docs/yaml-conventions.md`) or for any other type.
    /// </summary>
    [YamlMember(Order = 21)]
    public string? RelationshipSchemaName { get; init; }

    /// <summary>
    /// Lookup only: the new relationship's Behavior — "Referential",
    /// "ReferentialRestrictDelete", or "Parental" (see
    /// <see cref="Dataverse.RelationshipBehaviors"/>). Only present when set
    /// to something other than "Referential" — the Maker UI's own default
    /// for a brand-new lookup, and the only one of the three that never
    /// collides with another Parental relationship the referencing table
    /// might already have (see `docs/yaml-conventions.md`).
    /// </summary>
    [YamlMember(Order = 22)]
    [Schema.SchemaEnum(typeof(Dataverse.RelationshipBehaviors), nameof(Dataverse.RelationshipBehaviors.Names))]
    public string? RelationshipBehavior { get; init; }
}

using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated, hand-designed YAML shape for a single Dynamics view (a Dataverse
/// <c>savedquery</c> record) — the <see cref="Models.EntityDefinition"/>
/// counterpart for views. Sourced live from the Dataverse Web API (see
/// <see cref="Dataverse.IDataverseClient.GetViewDefinitionsJsonAsync"/>).
///
/// Unlike <see cref="EntityDefinition"/>, there's no separate XML-vs-JSON
/// reader split here: <c>fetchxml</c> and <c>layoutxml</c> come back from the
/// Web API as plain string properties on an otherwise ordinary Dataverse
/// record, not wrapped in the metadata API's managed-property shapes — so
/// one reader (<see cref="ViewJsonDefinitionReader"/>) is enough.
///
/// Deliberately excluded: <c>savedqueryid</c>. It's a GUID assigned at
/// creation with no meaning to a human editing this YAML, and — unlike an
/// entity's LogicalName or an attribute's SchemaName — Dataverse doesn't
/// require it as input when creating a view; <see cref="Name"/> is this
/// asset's only practical identity. Export keeps FetchXml/LayoutXml verbatim
/// rather than decomposing them — see <see cref="ViewJsonDefinitionReader"/>
/// for why; a hand-written view may give <see cref="Columns"/> instead of a
/// LayoutXml.
/// </summary>
public sealed class ViewDefinition
{
    /// <summary>The view's display name, e.g. "Active Accounts" — written as the top-level "view" key.</summary>
    [YamlMember(Alias = "view", Order = 0)]
    public required string Name { get; init; }

    /// <summary>Logical name of the table this view belongs to, e.g. "account" (the savedquery's <c>returnedtypecode</c>).</summary>
    [YamlMember(Order = 1)]
    public required string Entity { get; init; }

    /// <summary>
    /// The view's <c>savedqueryid</c>. Import uses it, when present, to pick
    /// the target view directly rather than by table + name - which can match
    /// more than one live view (two views may share a name) and is then
    /// refused as ambiguous. An id that no longer exists live is an error,
    /// never a fallback to the name. Omit to match by table + name; export
    /// always writes it.
    /// </summary>
    [YamlMember(Order = 2)]
    public Guid? SavedQueryId { get; init; }

    /// <summary>The view's description.</summary>
    [YamlMember(Order = 3)]
    public string? Description { get; init; }

    /// <summary>
    /// The kind of view this is, e.g. "QuickFindSearch" or "LookupView" —
    /// see https://learn.microsoft.com/dotnet/api/microsoft.crm.sdk.savedqueryquerytype
    /// for the full set of values. Only present when it's something other
    /// than "MainApplicationView" — an ordinary system view, and by far the
    /// most common case. Omit for an ordinary view; applying this file back
    /// won't change its type.
    /// </summary>
    [YamlMember(Order = 4)]
    public string? QueryType { get; init; }

    /// <summary>
    /// Only present when true — a table typically has one default view per
    /// query type, and false is the common case for any given view. Omit
    /// to leave this as a non-default view; applying this file back won't
    /// make it the default.
    /// </summary>
    [YamlMember(Order = 5)]
    public bool? IsDefault { get; init; }

    /// <summary>
    /// Whether this view's columns are searched by Quick Find. Only present
    /// when true. Omit to leave Quick Find search off for this view;
    /// applying this file back won't turn it on.
    /// </summary>
    [YamlMember(Order = 6)]
    public bool? IsQuickFindQuery { get; init; }

    /// <summary>
    /// Only present when true — most views ship as part of the platform or
    /// a solution, not hand-created by a user. Purely informational:
    /// applying this file back never changes it either way.
    /// </summary>
    [YamlMember(Order = 7)]
    public bool? IsUserDefined { get; init; }

    /// <summary>
    /// Only present when false — true (customizable) is the common case.
    /// Omit to leave this view customizable; applying this file back won't
    /// lock it down.
    /// </summary>
    [YamlMember(Order = 8)]
    public bool? IsCustomizable { get; init; }

    /// <summary>
    /// The view's query, as FetchXML. Null for a handful of internal system
    /// views (e.g. "{Entity} BulkOperation View") that Dataverse doesn't
    /// populate this on.
    /// </summary>
    [YamlMember(Order = 9, ScalarStyle = ScalarStyle.Literal)]
    public string? FetchXml { get; init; }

    /// <summary>The view's column layout, as LayoutXML. Null for views that don't render a grid (e.g. some Quick Find configurations).</summary>
    [YamlMember(Order = 10, ScalarStyle = ScalarStyle.Literal)]
    public string? LayoutXml { get; init; }

    /// <summary>
    /// A friendlier alternative to <see cref="LayoutXml"/> for hand-written views: the
    /// columns to show, in order. Each entry is a plain string (the column's logical
    /// name, default width 150) or <c>{ name, width }</c>. On import the tool builds the
    /// layout from this, adds any listed column missing from <see cref="FetchXml"/> and
    /// always includes the table's id column there, and fills in the table's type code.
    /// With no <see cref="FetchXml"/>, an existing view's live FetchXML is the base, and a
    /// new view queries just these columns. Giving both this and <see cref="LayoutXml"/> is
    /// refused. Never written by <c>view export</c>, which keeps writing the raw
    /// <see cref="LayoutXml"/>.
    /// </summary>
    [YamlMember(Order = 11)]
    public IReadOnlyList<ViewColumn>? Columns { get; init; }
}

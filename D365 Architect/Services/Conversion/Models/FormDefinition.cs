using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated, hand-designed YAML shape for a single Dynamics form (a Dataverse
/// <c>systemform</c> record) — the <see cref="ViewDefinition"/> counterpart
/// for forms. Sourced live from the Dataverse Web API (see
/// <see cref="Dataverse.IDataverseClient.GetFormDefinitionsJsonAsync"/>).
///
/// Unlike a view's FetchXML/LayoutXML, FormXML isn't kept verbatim: a raw
/// blob of it is unreadable and undiffable at a glance, so
/// <see cref="FormJsonDefinitionReader"/> decomposes it into <see cref="Tabs"/>/
/// <see cref="HeaderControls"/>/<see cref="FooterControls"/> instead —
/// enough to actually act on (review, or drive a bulk change across many
/// forms), not just enough to tell that something's there. Every control on
/// the form is captured, not only simple data-bound fields — see
/// <see cref="FormControl"/>'s own doc comment for how a subgrid/web
/// resource/iframe/quick view control/timeline stays fully represented
/// without this tool having to hand-model each one's parameter shape.
/// Dashboards remain a real gap, though: a dashboard's tiles are
/// `&lt;Visualization&gt;`/`&lt;SavedQuery&gt;` elements, not `&lt;control&gt;`
/// elements at all, so they come back with tabs/sections but no controls in
/// them — undecomposed rather than guessed at, same spirit as a view's
/// undecomposed LayoutXML columns.
///
/// Deliberately excluded: <c>formid</c>/<c>formidunique</c> — GUIDs with no
/// meaning to a human editing this YAML; <see cref="Name"/> is this asset's
/// only practical identity, same reasoning as <see cref="ViewDefinition.Name"/>.
/// Also excluded: <c>formpresentation</c> (Classic/Air/ConvertedIC) — a
/// rendering detail rather than something worth round-tripping until a real
/// need for it shows up.
///
/// This model was audited property-by-property against Microsoft's own
/// FormXML schema (https://learn.microsoft.com/power-apps/developer/model-driven-apps/form-xml-schema)
/// and against every field/form actually exported in that audit, not just
/// spot-checked — see `docs/yaml-conventions.md` for the full table of what
/// that covered. What's deliberately still out of scope, confirmed present
/// in real FormXML but not decomposed here: `&lt;Navigation&gt;` (related-record
/// nav menu ordering/visibility), `&lt;clientresources&gt;` (JS/CSS resource
/// declarations, largely redundant with <see cref="Events"/>'s own library
/// references), and rare cell flags (`ischartcell`/`isstreamcell`/`istilecell`,
/// legacy Interactive Service Hub artifacts). Confirmed absent from every
/// form exported so far, so left unimplemented rather than guessed at:
/// `&lt;RibbonDiffXml&gt;` (per-form command bar customization),
/// `&lt;formparameters&gt;`/`&lt;externaldependencies&gt;`, and a tab's own
/// `tabheader`/`tabfooter` (distinct from the form-level ones this model
/// already captures). Also excluded: the form root's own display attributes
/// (`showImage`, `shownavigationbar`, `maxWidth`, `hasmargin`, and
/// `headerdensity`/`showinformselector`, which aren't even in Microsoft's
/// published schema) — chrome/rendering settings for the whole form shell,
/// same reasoning as <c>formpresentation</c> above.
///
/// None of that is necessarily lost forever on the way back out, though:
/// <c>form build-xml</c> patches onto the form's existing live FormXML
/// rather than building a new document from scratch whenever one already
/// exists, so all of the above survives untouched in that case — see
/// <see cref="FormXmlWriter"/>'s own doc comment for exactly how.
/// </summary>
public sealed class FormDefinition
{
    /// <summary>The form's display name, e.g. "Account Main Form". Serialises as the top-level "form" key.</summary>
    [YamlMember(Alias = "form", Order = 0)]
    public required string Name { get; init; }

    /// <summary>Logical name of the table this form belongs to, e.g. "account" (the systemform's <c>objecttypecode</c>).</summary>
    [YamlMember(Order = 1)]
    public required string Entity { get; init; }

    [YamlMember(Order = 2)]
    public string? Description { get; init; }

    /// <summary>
    /// The kind of form this is, e.g. "Quick Create" or "Dashboard" — the
    /// systemform "type" option set's own label (see
    /// https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/systemform).
    /// Only present when it's something other than "Main" — an ordinary
    /// main form, and by far the most common case. Omit for a Main form;
    /// applying this file back won't change its type.
    /// </summary>
    [YamlMember(Order = 3)]
    public string? Type { get; init; }

    /// <summary>
    /// Only present when true — a table typically has one default form per
    /// <see cref="Type"/>, and false is the common case for any given form.
    /// Omit to leave this as a non-default form; applying this file back
    /// won't make it the default.
    /// </summary>
    [YamlMember(Order = 4)]
    public bool? IsDefault { get; init; }

    /// <summary>
    /// "Inactive" for a form that hasn't been published/activated yet.
    /// Absent — Dataverse's "Active" state — is the common case for any
    /// form that's actually in use. Omit to leave this form active;
    /// applying this file back won't deactivate it.
    /// </summary>
    [YamlMember(Order = 5)]
    public string? FormActivationState { get; init; }

    /// <summary>
    /// Only present when false — true (customizable) is the common case;
    /// see <see cref="DefaultValueConventions.FalseOrNull"/>. Omit to leave
    /// this form customizable; applying this file back won't lock it down.
    /// </summary>
    [YamlMember(Order = 6)]
    public bool? IsCustomizable { get; init; }

    /// <summary>
    /// The form's tabs, decomposed from its FormXML — see this class's own
    /// doc comment for what is (and isn't) captured. Empty for a form this
    /// tool doesn't decompose at all yet (dashboards) or whose FormXML
    /// Dataverse didn't return.
    /// </summary>
    [YamlMember(Order = 7)]
    public IReadOnlyList<FormTab> Tabs { get; init; } = [];

    /// <summary>Controls pinned to the form's header bar, outside any tab. Absent when the form has none.</summary>
    [YamlMember(Order = 8)]
    public IReadOnlyList<FormControl>? HeaderControls { get; init; }

    /// <summary>Controls pinned to the form's footer bar, outside any tab. Absent when the form has none.</summary>
    [YamlMember(Order = 9)]
    public IReadOnlyList<FormControl>? FooterControls { get; init; }

    /// <summary>
    /// Which form this one is derived from (FormXML's `&lt;ancestor&gt;`) —
    /// common on an Interactive-experience-style form built from an older
    /// one. Absent when this form has no ancestor.
    /// </summary>
    [YamlMember(Order = 10)]
    public string? Ancestor { get; init; }

    /// <summary>Fields the form tracks but never renders on any tab — see <see cref="FormHiddenField"/>. Absent when there are none.</summary>
    [YamlMember(Order = 11)]
    public IReadOnlyList<FormHiddenField>? HiddenFields { get; init; }

    /// <summary>When this form is offered as a choice for a record, and to whom — see <see cref="FormDisplayCondition"/>. Absent when the form has none (every published Main form seen has one, but not every form type does).</summary>
    [YamlMember(Order = 12)]
    public FormDisplayCondition? DisplayCondition { get; init; }

    /// <summary>JavaScript libraries this form loads. Absent when there are none.</summary>
    [YamlMember(Order = 13)]
    public IReadOnlyList<FormLibrary>? Libraries { get; init; }

    /// <summary>The form's business logic — event/handler bindings, not layout. See <see cref="FormEvent"/>. Absent when there are none.</summary>
    [YamlMember(Order = 14)]
    public IReadOnlyList<FormEvent>? Events { get; init; }
}

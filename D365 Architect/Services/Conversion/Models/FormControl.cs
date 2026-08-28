using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// A single control on a <see cref="FormDefinition"/> — one `&lt;cell&gt;`/`&lt;control&gt;`
/// pair out of its FormXML, decomposed rather than left as raw XML (see
/// <see cref="FormDefinition"/>'s doc comment).
///
/// Every control is captured here, not just simple data-bound fields:
/// subgrids, web resources, iframes, quick view controls, and timelines all
/// show up too — <see cref="Field"/> is null for those, since they don't
/// bind to a single attribute the way a text box or lookup does. What makes
/// each of those useful (a subgrid's target table/view/relationship, a web
/// resource's name, ...) lives in <see cref="Parameters"/> rather than its
/// own hand-modeled property per control type: FormXML has well over a
/// dozen control types, each with its own parameter shape, and hand-picking
/// which properties matter for each one this tool hasn't verified against a
/// real tenant risks silently dropping exactly the detail someone needs to
/// act on. Converting the whole `&lt;parameters&gt;` block structurally
/// (rather than modeling a curated subset of it) means nothing is lost
/// before a specific control type earns its own first-class properties.
/// </summary>
public sealed class FormControl
{
    /// <summary>The control's internal id, e.g. "name" or "CasesForCustomer" (FormXML's `control` element's own `id`).</summary>
    [YamlMember(Order = 0)]
    public required string Id { get; init; }

    /// <summary>
    /// The attribute logical name this control is bound to, e.g. "name"
    /// (FormXML's `datafieldname`). Null for a control that isn't bound to
    /// a single field — a subgrid, web resource, iframe, quick view
    /// control, or timeline.
    /// </summary>
    [YamlMember(Order = 1)]
    public string? Field { get; init; }

    [YamlMember(Order = 2)]
    public string? Label { get; init; }

    /// <summary>
    /// Dataverse's raw control class id (a GUID) — which control renders
    /// this cell, e.g. distinguishing a non-default control override from a
    /// field's usual one. Deliberately not mapped to a friendly name:
    /// unlike a solutioncomponent componenttype (confirmed straight from
    /// Microsoft's own docs), no equally authoritative source lists every
    /// control class id, and guessing one wrong would be worse than showing
    /// the raw id.
    /// </summary>
    [YamlMember(Order = 3)]
    public string? ClassId { get; init; }

    /// <summary>
    /// Only present when true — most controls on a form are enabled/editable.
    /// Omit to leave this control enabled; applying this file back won't
    /// disable it.
    /// </summary>
    [YamlMember(Order = 4)]
    public bool? Disabled { get; init; }

    /// <summary>
    /// Only present when false — most controls on a form are visible.
    /// Confirmed live rather than assumed: a real, deliberately hidden
    /// field (FormXML's `visible="false"`) is meaningfully different from
    /// one simply not on the form at all, and this tool would otherwise
    /// show them identically. Omit to leave this control visible; applying
    /// this file back won't hide it.
    /// </summary>
    [YamlMember(Order = 5)]
    public bool? Visible { get; init; }

    /// <summary>
    /// How many of the section's own sub-columns this control's cell spans
    /// (FormXML's `colspan`, an attribute of `&lt;cell&gt;`, not `&lt;control&gt;`).
    /// Only present when greater than 1 — a single column is the common
    /// case; applying this file back won't change a cell's own span.
    /// Confirmed live: real, non-default spans exist alongside the
    /// overwhelmingly common `colspan="1"`, not just the trivial case.
    /// </summary>
    [YamlMember(Order = 6)]
    public int? ColumnSpan { get; init; }

    /// <summary>
    /// How many rows this control's cell spans (FormXML's `rowspan`) —
    /// genuinely structural, not cosmetic: a tall control like a subgrid or
    /// notes timeline is laid out this way specifically so it visually
    /// occupies several of its section's otherwise-empty rows rather than
    /// being squeezed into one. Only present when greater than 1 — a single
    /// row is the common case; applying this file back won't change a
    /// cell's own span. Confirmed live: real spans up to 15 alongside the
    /// overwhelmingly common `rowspan="1"`.
    /// </summary>
    [YamlMember(Order = 7)]
    public int? RowSpan { get; init; }

    /// <summary>
    /// This control's own `&lt;parameters&gt;` block, converted structurally
    /// (each XML element becomes a YAML map key, using the element's own
    /// name; an attribute becomes a plain key under a nested `attributes`
    /// map, its own text alongside those under `value`) rather than left as
    /// raw XML text — e.g. a subgrid's target table, relationship, and
    /// view; a web resource's name; a quick view control's source table and
    /// form. A boolean value of `false` inside this block is left out —
    /// every one of these parameters is an optional XML boolean with no
    /// platform default declared, so an absent one and an explicit `false`
    /// mean the same thing to Dataverse; omitting it here changes nothing
    /// when applied back. `true` is always kept. This whole property is
    /// absent when the control has no parameters at all, or when every one
    /// it had was `false` (see `docs/yaml-conventions.md` for the full
    /// reasoning). Unlike <see cref="ClassId"/>, none of this needs a
    /// friendly-name lookup this tool would have to get right on its own:
    /// the XML's own element/attribute names are kept as-is.
    /// </summary>
    [YamlMember(Order = 8)]
    public object? Parameters { get; init; }

    /// <summary>
    /// Alternate controls attached to this one via the form designer's
    /// "add a component" feature — e.g. a Calendar control added to a
    /// subgrid, or per-client (Web/Phone/Tablet) replacements. See
    /// <see cref="FormAdditionalControl"/>'s own doc comment. Absent when
    /// this control has none.
    /// </summary>
    [YamlMember(Order = 9)]
    public IReadOnlyList<FormAdditionalControl>? AdditionalControls { get; init; }

    /// <summary>
    /// Field-level event bindings scoped to this control specifically (e.g.
    /// an "on change" handler for this one field) — distinct from
    /// <see cref="FormDefinition.Events"/>, which are form-wide. Confirmed
    /// live rather than assumed: Microsoft's own FormXML schema *documentation
    /// page* doesn't mention `&lt;events&gt;` as a valid child of a cell at
    /// all (only `&lt;labels&gt;`/`&lt;control&gt;` are) — the actual
    /// downloadable XSD (see <see cref="FormXmlValidator"/>) already
    /// declares it correctly, but the prose page doesn't, exactly the kind
    /// of gap a docs-only audit would have missed. See
    /// <see cref="FormEvent"/>. Absent when this control has none.
    /// </summary>
    [YamlMember(Order = 10)]
    public IReadOnlyList<FormEvent>? Events { get; init; }
}

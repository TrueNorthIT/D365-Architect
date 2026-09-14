using D365Architect.Services.Conversion;
using D365Architect.Services.Schema;
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

    /// <summary>The field's display label on the form.</summary>
    [YamlMember(Order = 2)]
    public string? Label { get; init; }

    /// <summary>
    /// This label's text in every language besides the one shown as
    /// <see cref="Label"/>, keyed by Dataverse's own languagecode (e.g.
    /// 1036 for French). Absent on a single-language tenant (the
    /// overwhelming common case) or when this control has only its own.
    /// </summary>
    /// <remarks>
    /// Previously every translation but the primary one was silently
    /// discarded on export — confirmed as a real gap, not a stripped
    /// default: this is genuine maker-authored text on a multi-language
    /// tenant, lost permanently on every round-trip until this was added.
    /// </remarks>
    [YamlMember(Order = 3)]
    public IReadOnlyDictionary<int, string>? Translations { get; init; }

    /// <summary>
    /// Which control renders this cell — a Dataverse standard control by
    /// name, e.g. "SingleLineText", "Lookup", "Subgrid". Use
    /// customControlId instead for a custom/PCF control or one not in this
    /// list.
    /// </summary>
    /// <remarks>
    /// See <see cref="StandardFormControls"/> for the full list and how
    /// each entry was confirmed against real, live Dataverse output rather
    /// than guessed. Mutually exclusive with <see cref="CustomControlId"/>.
    /// </remarks>
    [YamlMember(Order = 4)]
    [SchemaEnum(typeof(StandardFormControls), nameof(StandardFormControls.FriendlyNames))]
    public string? Control { get; init; }

    /// <summary>
    /// The control's raw Dataverse class id (a GUID), for a custom/PCF
    /// control or a standard one not yet recognized by name. Use control
    /// instead when the control is one of Dataverse's own standard ones.
    /// </summary>
    /// <remarks>
    /// Kept raw rather than mapped to a friendly name here: unlike
    /// Dataverse's own standard controls (a small, confirmed set — see
    /// <see cref="StandardFormControls"/>), there's no source enumerating
    /// every control ever registered on a real tenant, and a wrong guess
    /// would misrepresent real data rather than just under-describe it.
    /// </remarks>
    [YamlMember(Order = 5)]
    public string? CustomControlId { get; init; }

    /// <summary>
    /// Deprecated — use control or customControlId instead. Still
    /// recognized for compatibility with a file exported before those
    /// existed, but never written by a fresh export.
    /// </summary>
    [YamlMember(Alias = "classId", Order = 99)]
    public string? ClassId { get; init; }

    /// <summary>
    /// Only present when true — an unbound lookup control (its own
    /// `TargetEntities` parameter list names which tables it can point at,
    /// rather than the control being bound to a real relationship). Omit
    /// for an ordinary bound control; applying this file back won't unbind
    /// it.
    /// </summary>
    [YamlMember(Order = 6)]
    public bool? IsUnbound { get; init; }

    /// <summary>
    /// Only present when true — most controls on a form are enabled/editable.
    /// Omit to leave this control enabled; applying this file back won't
    /// disable it.
    /// </summary>
    [YamlMember(Order = 7)]
    public bool? Disabled { get; init; }

    /// <summary>
    /// Only present when true — this field is forced "Business Required" on
    /// this form specifically, independent of the column's own
    /// metadata-level requirement level. Omit for the common case (no
    /// form-level override); applying this file back won't add one.
    /// </summary>
    [YamlMember(Order = 8)]
    public bool? IsRequired { get; init; }

    /// <summary>
    /// Only present when false — a deliberately hidden field, distinct from
    /// one simply not on the form at all. Omit to leave this control
    /// visible; applying this file back won't hide it.
    /// </summary>
    [YamlMember(Order = 9)]
    public bool? Visible { get; init; }

    /// <summary>
    /// Only present when false — this control's own field label is
    /// deliberately hidden (common on a subgrid, which already shows its
    /// own title bar and doesn't need a redundant label above it). Omit to
    /// leave this control's label shown; applying this file back won't hide
    /// it.
    /// </summary>
    [YamlMember(Order = 10)]
    public bool? ShowLabel { get; init; }

    /// <summary>
    /// Whether this control shows on the phone-optimized layout —
    /// FormXML's `availableforphone` attribute, shown exactly as stated
    /// rather than defaulted/stripped like this file's other booleans:
    /// which direction (true or false) is the common case for this
    /// specific attribute hasn't been confirmed against real tenant
    /// samples, so nothing is assumed either way. Absent when this
    /// control's FormXML doesn't set it at all.
    /// </summary>
    [YamlMember(Order = 11)]
    public bool? AvailableOnPhone { get; init; }

    /// <summary>
    /// How many of the section's own sub-columns this control's cell spans.
    /// Only present when greater than 1 — a single column is the common
    /// case; applying this file back won't change a cell's own span.
    /// </summary>
    [YamlMember(Order = 12)]
    public int? ColumnSpan { get; init; }

    /// <summary>
    /// How many rows this control's cell spans — used to make a tall
    /// control like a subgrid or notes timeline visually occupy several
    /// otherwise-empty rows rather than being squeezed into one. Only
    /// present when greater than 1 — a single row is the common case;
    /// applying this file back won't change a cell's own span.
    /// </summary>
    [YamlMember(Order = 13)]
    public int? RowSpan { get; init; }

    /// <summary>
    /// This control's own settings — e.g. a subgrid's target table,
    /// relationship, and view; a web resource's name; a quick view
    /// control's source table and form. Each setting keeps its own
    /// platform name as its key; a setting with its own sub-settings
    /// becomes a nested map. Absent when the control has none set.
    /// </summary>
    /// <remarks>
    /// Converted structurally from FormXML's `&lt;parameters&gt;` block
    /// (each XML element becomes a YAML map key; an attribute becomes a
    /// plain key under a nested `attributes` map, its own text alongside
    /// those under `value`) rather than left as raw XML text — see
    /// `docs/yaml-conventions.md` for the full conversion rules. A boolean
    /// value of `false` inside this block is left out (an absent one and
    /// an explicit `false` mean the same thing to Dataverse; omitting it
    /// changes nothing when applied back), `true` is always kept.
    /// </remarks>
    [YamlMember(Order = 14)]
    public object? Parameters { get; init; }

    /// <summary>
    /// Alternate controls attached to this one via the form designer's
    /// "add a component" feature — e.g. a Calendar control added to a
    /// subgrid, or per-client (Web/Phone/Tablet) replacements. Absent when
    /// this control has none.
    /// </summary>
    [YamlMember(Order = 15)]
    public IReadOnlyList<FormAdditionalControl>? AdditionalControls { get; init; }

    /// <summary>
    /// Field-level event bindings scoped to this control specifically (e.g.
    /// an "on change" handler for this one field) — distinct from the
    /// form-wide event bindings at the top level. Absent when this control
    /// has none.
    /// </summary>
    [YamlMember(Order = 16)]
    public IReadOnlyList<FormEvent>? Events { get; init; }
}

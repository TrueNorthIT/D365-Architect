# YAML conventions

This document describes design rules this tool's exported YAML (`*.table.yml`,
`*.view.yml`, `*.form.yml`) follows, and why. Two audiences:

- **Whoever builds the other direction** (`d365architect form build-xml`
  exists today and rebuilds FormXML from a `*.form.yml` file — see
  "Rebuilding FormXML" below; a live `form import` that pushes it back into
  Dataverse doesn't exist yet) needs to know exactly what an absent field
  means and how a converted structure maps back to its source shape —
  that's most of what's below.
- **Anyone editing the YAML by hand** needs the same thing, just surfaced
  where they'll actually see it: every rule here is also written into the
  field descriptions in `schema export`'s generated JSON Schema
  (`schema/*.schema.json`), so it shows up as a hover tooltip while editing
  a `*.table.yml`/`*.view.yml`/`*.form.yml` file in an editor with YAML
  language server support (this repo's `.vscode/settings.json` wires that up
  already) — not just here.

Nothing here is user-facing CLI documentation — see the root
[`README.md`](../README.md) for that.

## Rule 1: an absent field means "left at Dataverse's own default", never "unknown"

Every reader in this tool (`EntityJsonDefinitionReader`, `EntityXmlDefinitionReader`,
`ViewJsonDefinitionReader`, `FormJsonDefinitionReader`) follows the same
contract: if a value equals whatever Dataverse itself would use when nothing
was ever set, it's left out of the YAML rather than restated on every single
export. This keeps the common case readable, and it means a reader of the
YAML — human or, eventually, an import command — can trust that "not
present" always means one specific thing, never "we didn't check."

The shared helpers live in `DefaultValueConventions.cs`:

| Helper | Meaning |
|---|---|
| `RequiredLevelOrNull` | Omits `"None"` — Dataverse's default `RequiredLevel` for every column. |
| `TrueOrNull` | Keeps a flag only when `true`, for flags whose platform/common default is `false`. |
| `FalseOrNull` | The inverse — keeps a flag only when `false`, for flags whose common default is `true`. |

**For import**: an absent field in the YAML should translate to *not setting
that property* in the create/update request (or setting it to the specific
default value named below) — never treated as "the user didn't decide,
leave whatever's already there" for a field the export explicitly chose to
omit.

### Every field this applies to today

| Model | Field | Omitted when | Confirmed via |
|---|---|---|---|
| `AttributeDefinition` | `RequiredLevel` | `"None"` | Dataverse's documented column default |
| `AttributeDefinition` | `IsCustomField` | `false` | Platform default for a column |
| `EntityDefinition` | `IsActivity`, `HasActivities`, `HasNotes` | `false` | Documented defaults for a newly-created table |
| `ViewDefinition` | `QueryType` | `0` / "MainApplicationView" | `Microsoft.Crm.Sdk.SavedQueryQueryType`'s own naming — an ordinary system view is overwhelmingly the common case |
| `ViewDefinition` | `IsDefault`, `IsQuickFindQuery`, `IsUserDefined` | `false` | Common case for any single view |
| `ViewDefinition`, `FormDefinition` | `IsCustomizable` | `true` (**inverted** — see `FalseOrNull`) | Confirmed against every view/form exported this session: `true` with zero exceptions except a handful of genuinely locked-down internal system views/forms, which correctly still show `false` |
| `FormDefinition` | `Type` | `2` / "Main" | The systemform `type` option set's own documented labels — Main is the ordinary case |
| `FormDefinition` | `IsDefault` | `false` | Common case for any single form |
| `FormDefinition` | `FormActivationState` | `1` / "Active" | Common case for any form actually in use (Inactive = an unpublished draft) |
| `FormControl` | `Disabled` | `false` | Common case — most controls are enabled |
| `FormControl` | `Visible` | `true` (**inverted** — see `FalseOrNull`) | Common case — most controls are visible; `false` confirmed live on 11 real fields across 5 forms |
| `FormTab`, `FormSection` | `Visible` | `true` (**inverted** — see `FalseOrNull`) | Same structural argument as `FormControl.Visible`: FormXML's `<tab>`/`<section>` `visible` attribute is `xs:boolean`, optional, no XSD default — a tab/section is shown unless deliberately hidden, so omission means visible. Previously not modelled at all (not just unstripped-but-present — genuinely absent from `FormTab`/`FormSection`), so a hidden tab/section silently came back visible on every export/import round-trip until fixed. |
| `FormControl`, `FormSection` | `ShowLabel` | `true` (**inverted** — see `FalseOrNull`) | Same story as `Visible` above, one attribute over: FormXML's cell-level and section-level `showlabel` is `xs:boolean`, optional, no XSD default — a label is shown unless deliberately hidden (common on a subgrid, which already has its own title bar). Also previously absent from both models entirely, so a hidden subgrid/section label silently came back shown on every round-trip until fixed. |
| `FormDisplayCondition` | `FallbackForm` | `false` | At most one form per table can be the fallback, so `false` is definitionally the common case across a table's forms as a whole, not just an observed majority |
| `FormControl` | `ColumnSpan`, `RowSpan` | `1` | FormXML's own `colspan`/`rowspan` attributes have no declared default, but `1` (no spanning) is overwhelmingly the common case; confirmed live on a single real form's 69 cells (64/69 `rowspan` and 63/69 `colspan` values were exactly `1`), with real, non-default spans (up to `rowspan="15"`, for a subgrid/timeline control deliberately laid out to occupy several otherwise-empty rows) shown whenever they occur |
| `FormControl` | `Parameters` (every boolean value inside it) | `false` | See Rule 3 below — a different, stronger argument than the others in this table |

**Not covered by this convention, deliberately**: `FormEvent.Active`, `FormEventHandler.Enabled`/`PassExecutionContext`. Every sample seen states these explicitly (always `true`), but with no observed unset or `false` case, there's no evidence for what omitting them would actually mean — so they're shown exactly as FormXML states them rather than guessed at.

## Rule 2: the YAML doesn't have to mirror the source format's own conventions — only be reconstructable from

When converting something structurally (right now, only a `FormControl`'s
FormXML `<parameters>` block — see `FormJsonDefinitionReader.ConvertToObject`),
this tool is free to reshape it however reads best, as long as import can
still derive the original from it. Concretely:

- An XML attribute converts to a plain key under an `attributes` map, e.g.
  `<QuickFormId entityname="account">` → `attributes: { entityname: account }`.
- An element's own text, when it also has attributes, converts to a `value`
  key alongside them.
- A repeated child element name converts to a YAML list.
- Element/attribute **names** are kept exactly as Dataverse names them (e.g.
  `RelationshipName`, `TargetEntityType`, `entityname`) — never re-cased or
  reworded, since guessing at word boundaries risks getting it wrong the same
  way guessing at a custom control's class id's meaning would (see Rule 4).
- A parameter value that is itself a double-encoded XML fragment (e.g. a
  quick view control's `QuickForms`) is parsed and converted the same way,
  recursively — but wrapped under a reserved `xml` key naming the
  fragment's own root element (e.g.
  `QuickForms: { xml: { QuickFormIds: { QuickFormId: ... } } }`), rather
  than merged in as if it were real structural children. Those two cases
  parse identically once the fragment's escaped text is unescaped and
  re-parsed as XML — the only way to tell them apart on the way back out is
  this marker. That distinction is worth keeping, not just cosmetic:
  Dataverse's own runtime expects `QuickForms`' value as escaped *text*, not
  literal XML structure, so writing the latter isn't just a schema
  nitpick — it was a real, confirmed `FormXmlWriter` bug (caught by a real
  `form build-xml` schema violation) before this marker existed.

This was a deliberate choice over the terser but cryptic `@name`/`#text`
XML-to-JSON convention (used by tools like Newtonsoft's `XmlNodeConverter`):
every key in this tool's YAML should read as a real word, not a sigil.

**For import**: reconstructing a `<parameters>` block from this YAML means
walking it back the other way — an `attributes`/`value` pair becomes an
element with that attribute and text; an `xml` key rebuilds its one nested
entry as a real element tree, then sets *that whole tree's own rendered
text*, re-escaped, as the current element's value rather than adding it as
a child; everything else becomes a child element named after its own key;
and a list becomes repeated elements with that name.

## Rule 3: a stripped `false` inside `parameters` is a special case, not the general rule

`FormControl.Parameters` (`ConvertToObject`) also drops any leaf value that
is literally `"false"` (case-insensitive) — including inside its
`attributes` map — while always keeping `"true"`. This looks like Rule 1,
but it rests on a different, narrower argument, and it's important import
understands why it doesn't extend further:

Every one of these parameters (subgrid, lookup, quick-view-collection
controls, ...) is declared in Microsoft's own FormXML XSD schema as
`type="xs:boolean" minOccurs="0"`, with **no XSD `default="..."`**. A boolean
has only two possible states — so when the element is entirely absent, it
has to mean one of `true`/`false`, and since there's no declared default, the
only value that makes the schema self-consistent is `false`. That means
*omitting* the element and writing it explicitly as `false` are the same
thing to Dataverse either way — so dropping a literal `false` here loses
nothing reconstructable. This was double-checked empirically too: aggregated
every boolean parameter value across every form exported in this session
(179 files) before applying it, and `false` was either unanimous or the
overwhelming majority for every one of them, with the rare `true` always
still shown.

**This reasoning is boolean-specific.** It's exactly why non-boolean
parameter values (`RecordsPerPage`, `TargetEntityType`, view/relationship
GUIDs, ...) are still shown unconditionally — there's no equivalent
"only two possible states" argument for a string, GUID, or number, and no
single documented default across the well-over-a-dozen control types these
parameters span. Guessing one there risks silently hiding real
configuration. **For import**: when a boolean parameter is absent from the
YAML, simply don't emit that XML element at all (don't write it as `false`
explicitly) — either is correct, but omitting matches what export itself
does.

**Exception: anything inside a `data-set` node.** The "omitted ≡ false, so
Dataverse can't tell the difference" argument above only holds for controls
the static FormXml.xsd actually governs. A `data-set`-wrapped block belongs
to a PCF custom control instead (e.g. `ActivityCalendarControl`'s
`data-set name="Calendar"`), whose dataset binding Dataverse validates at
import time against the control's own manifest, not this XSD — there,
a boolean node's structural *presence* can itself be meaningful, not just
its value. Confirmed live: a `data-set` missing `IsUserView` (present as
`false` on export, stripped by this rule, never restored) was rejected on
import with `The dataset 'Calendar' should contain ViewId, IsUserView, or
both nodes` — the node's presence is what tells Dataverse whether `ViewId`
refers to a system or a personal view, so dropping it isn't the no-op it is
for the XSD-governed controls this rule was validated against.
`ConvertToObject` tracks an `insideDataSet` flag through the recursion and
never strips `false` once inside a `data-set` node, for exactly this reason.

## Rule 4: capture verbatim, unstripped, when a default isn't confirmed yet

`TrueOrNull`/`FalseOrNull` (Rule 1) both require the stripped direction to
already be confirmed — against Microsoft's docs, or by aggregating real
samples, or (Rule 3) by a structural argument. Several boolean attributes
added to `FormTab`/`FormSection`/`FormControl` from a systematic XSD audit
(`availableforphone` at all three levels, a tab's own `collapsible`) don't
have that yet: no live sample has been seen with either value, so which
direction is "the common case worth omitting" genuinely isn't known. Rather
than guess (and risk repeating the exact mistake that made `IsUserView`,
tab/section `visible`, and cell/section `showlabel` real bugs — assuming the
wrong thing about what "omitted" means), these are modelled as plain
nullable booleans shown exactly as FormXML states them, present only when
the source XML actually sets the attribute at all, in either direction. Once
a live sample confirms one direction is overwhelmingly common, these are
candidates to move to `TrueOrNull`/`FalseOrNull` like their siblings.

## FormXML coverage audit

FormXML is large enough that "does the reader handle everything in it" isn't
answerable by inspection alone. This table is the result of walking
Microsoft's own published schema (https://learn.microsoft.com/power-apps/developer/model-driven-apps/form-xml-schema)
element-by-element against `FormJsonDefinitionReader`, then checking real
occurrence counts across every form exported from two different tables
(`account`, `tn_inspection`) in a live tenant — not just spot-checked. Every
row is one of: captured, or a documented, deliberate decision not to —
"accounted for" either way, never silently missing.

| FormXML element/attribute | Status | Real occurrences | Notes |
|---|---|---|---|
| `tabs`/`columns`/`sections`/`rows`/`cells`/`control` | ✅ Captured | every form | `FormTab`/`FormColumn`/`FormSection`/`FormControl` |
| `header`/`footer` (form-level) | ✅ Captured | common | `FormDefinition.HeaderControls`/`FooterControls` |
| `control`'s own `<parameters>` | ✅ Captured | every non-trivial control | Structural conversion, see Rule 2/3 |
| `controlDescriptions`/`customControl` ("add a component") | ✅ Captured | several forms | `FormControl.AdditionalControls`, see `FormAdditionalControl` |
| Cell `visible="false"` | ✅ Captured | 11 (5 forms) | `FormControl.Visible` |
| Tab/section `visible="false"` | ✅ Captured | confirmed live (a hidden tab) | `FormTab.Visible`/`FormSection.Visible` — genuinely missing (not just unstripped) until this was found live: a hidden tab silently came back visible on every export/import round-trip, since neither model had a place to hold it at all |
| Cell/section `showlabel="false"` | ✅ Captured | confirmed live (a hidden subgrid/section label) | `FormControl.ShowLabel`/`FormSection.ShowLabel` — same "genuinely missing" story as tab/section `visible` above, found the same way; a tab's own `showlabel` stays excluded (see the chrome-attributes row below) since hiding a tab's label isn't the same kind of content change |
| A section's own `columns` attribute (sub-column count) | ✅ Captured | 4+ sections | `FormSection.Columns` |
| Cell `colspan`/`rowspan` | ✅ Captured | 5 non-default spans on one real form (up to `rowspan="15"`) | `FormControl.ColumnSpan`/`RowSpan` — genuinely structural (how much visual space a control's cell occupies), not cosmetic; found via a live round-trip check, not the initial schema audit |
| `ancestor` | ✅ Captured | 6 forms | `FormDefinition.Ancestor` |
| `hiddencontrols` | ✅ Captured | 7 forms | `FormDefinition.HiddenFields`, including its own `relationship` attribute (`FormHiddenField.Relationship`) — previously only 2 of its 3 attributes were captured, `relationship` silently wasn't |
| Non-primary-language `<label>` entries (`languagecode`/`description`) | ✅ Captured | not yet seen on a live multi-language tenant, added defensively | `FormTab.Translations`/`FormSection.Translations`/`FormControl.Translations` — previously every translation but the primary (English/1033, or first) was silently discarded on export; genuine maker-authored text, not a stripped default, permanently lost on a multi-language tenant's every round-trip until this was found |
| `availableforphone` (tab/section/cell) | ✅ Captured | not yet seen on a live sample, added defensively | `FormTab.AvailableOnPhone`/`FormSection.AvailableOnPhone`/`FormControl.AvailableOnPhone` — shown exactly as FormXML states it rather than defaulted/stripped like this file's other booleans: which direction is the common case at each of these three levels hasn't been confirmed live, so nothing is assumed |
| `control`'s own `isunbound`/`isrequired` | ✅ Captured | not yet seen on a live sample, added defensively | `FormControl.IsUnbound`/`IsRequired`, `TrueOrNull` (same direction as `Disabled`) — an unbound lookup or a form-level "Business Required" override are both the deliberate, uncommon case |
| A tab's own `collapsible` | ✅ Captured | not yet seen on a live sample, added defensively | `FormTab.Collapsible` — shown exactly as stated, not defaulted/stripped; unlike `showlabel`/`showbar`/etc. in the tab/section chrome row below, this is a real end-user interaction toggle (can this tab be collapsed at all), not a rendering hint |
| `DisplayConditions` (incl. `Role`/`Everyone`) | ✅ Captured | 21 forms | `FormDefinition.DisplayCondition` |
| `formLibraries` | ✅ Captured | 9 forms | `FormDefinition.Libraries` |
| `events` (form-level) | ✅ Captured | 11 forms | `FormDefinition.Events` |
| `events` nested inside a `<cell>` (field-level) | ✅ Captured | confirmed live | `FormControl.Events` — **not mentioned as valid there on Microsoft's own FormXML schema *documentation page***; found only by checking real data, not that page. The actual downloadable XSD already declares it correctly — see the callout below. |
| Dashboard tiles (`Visualization`/`SavedQuery`, not `<control>`) | 📝 Documented gap | every dashboard form | Not decomposed — see `FormDefinition`'s own doc comment |
| `Navigation` (related-record nav menu) | 📝 Documented gap | 11 forms | Real and non-trivial, but menu chrome rather than form content |
| `clientresources` (JS/CSS resource declarations) | 📝 Documented gap | 8 forms | Largely redundant with `events`'/`formLibraries`' own library references |
| Rare cell flags: `ischartcell`, `isstreamcell`, `istilecell` | 📝 Documented gap | 1 each | Legacy Interactive Service Hub artifacts |
| `RibbonDiffXml` (per-form command bar) | 📝 Documented gap | 0 | Its own large XML dialect; confirmed absent from every form checked, not just skipped |
| `formparameters`, `externaldependencies` | 📝 Documented gap | 0 | Confirmed absent from every form checked |
| A tab's own `tabheader`/`tabfooter` | 📝 Documented gap | 0 | Distinct from the form-level `header`/`footer` this tool already captures |
| Form root's own display attributes (`showImage`, `shownavigationbar`, `maxWidth`, `hasmargin`, `headerdensity`, `showinformselector`) | 📝 Documented gap | present on every form | Chrome/rendering settings for the form shell, not its content — same reasoning as the already-excluded `formpresentation` |
| Tab/section-level designer/rendering attributes (`locklevel`, `IsUserDefined`, `showbar`, `layout`, `celllabelalignment`, `celllabelposition`, `labelwidth`, `labelid`, `verticallayout`, `expanded`) | 📝 Documented gap | present on every tab/section | Same "chrome, not content" reasoning as the form root's own display attributes above, just at the tab/section level instead — extending that exclusion explicitly rather than leaving it implicit |
| A tab's own `showlabel` | 📝 Documented gap | present on every tab | Unlike a section's or a cell's own `showlabel` (now captured — see below), a tab's label is its own tab-strip entry; hiding it produces an unlabeled tab rather than a meaningful content change, so this stays grouped with the tab/section chrome attributes above |
| `control`'s own `indicationOfSubgrid="true"` | 📝 Documented gap | 3 (all subgrid controls) | Confirmed redundant with the control's own `classid`, which already identifies it as a subgrid — a designer-UI hint, not independent information |
| Cell `auto` (e.g. `auto="false"`) | 📝 Documented gap | 3, always alongside a spanning subgrid cell | Meaning unconfirmed — possibly whether the cell's span was auto-computed vs. manually set; no counter-example seen to guess from |
| `Handler`'s own `parameters` attribute (distinct from a control's `<parameters>` element) | 📝 Documented gap | every handler, always `""` | Same reasoning as `FormEvent.Active`/`FormEventHandler.Enabled` below: no observed non-empty value to show what omitting it would mean |

### Where the published schema itself turned out to be wrong

Microsoft actually publishes *two* different things that both get called
"the FormXML schema", and they don't agree with each other:

1. The prose [documentation page](https://learn.microsoft.com/power-apps/developer/model-driven-apps/form-xml-schema) — readable, but hand-maintained and, it turns out, incomplete.
2. The actual downloadable XSD (`FormXml.xsd` + its own `RibbonCore.xsd`/`RibbonTypes.xsd`/`RibbonWSS.xsd` includes), from the ["Schemas.zip"](https://learn.microsoft.com/power-apps/developer/model-driven-apps/edit-customizations-xml-file-schema-validation) download — the one this tool actually vendors and validates against (see `FormXmlValidator`, `Resources/FormXmlSchema/NOTICE.md`).

Three things this session originally found by checking real tenant data
against the *docs page*, since re-checked against the real XSD once it was
vendored for validation — two turned out to be the docs page's own gap, not
the XSD's:

- `controlDescriptions`/`customControl`'s `id` attribute is documented
  `use="required"` on the docs page; real forms have `customControl`
  entries with no `id` at all (only `name`/`formFactor`). **The real XSD
  already has this right** — `id` is declared `use="optional"`.
- A `<cell>` is documented as only ever containing `<labels>`/`<control>`;
  real forms nest a field-level `<events>` block directly inside one too
  (see the audit table above). **The real XSD already has this right
  too** — `FormXmlEventsType` is declared as a valid, optional child of a
  cell.
- A `<form>`'s root attributes documented on the page don't include
  `headerdensity` or `showinformselector`, both of which appear on real
  forms anyway. **This one genuinely is a gap in the real XSD as well**,
  confirmed by actually running a real form's FormXML through
  `FormXmlValidator`: `FormType`'s attribute list has no
  `xs:anyAttribute` wildcard to fall back on, so real, live
  Dataverse-produced FormXML fails strict validation against Microsoft's
  own official schema for these two attributes — through no fault of this
  tool's own output.

None of this is a reason to distrust either artifact generally — the real
XSD is still the authoritative source this tool validates against, and
still agrees with everything else this tool relies on (e.g. the
boolean-with-no-default argument in Rule 3) — just a reminder that "the
docs page doesn't mention it" and "the schema doesn't allow it" are two
different claims, and a `form build-xml` validation warning about
`headerdensity`/`showinformselector` reflects a real, pre-existing quirk of
Dataverse itself, not a bug introduced by rebuilding it through this tool.

**This specific pair of attributes is the only violation confirmed safe to
treat this way — it does not generalize.** A different violation (an
invalid child element inside a control's `<parameters>`) was once assumed
harmless on the same "schema vs. real Dataverse output disagree sometimes"
reasoning and turned out to make Dataverse's own write-time validation
reject the request outright with a 400 — see `FormXmlValidationMessage.
IsKnownHarmless` and the "Every rebuild is validated" section below for how
that changed `form import`'s behavior.

## Rule 4: raw platform identifiers are never guessed at

`AttributeDefinition`/`FormControl` keep some Dataverse identifiers as raw,
uninterpreted strings when there's no reliable way to do otherwise —
`FormControl.CustomControlId` (a custom/PCF control's own class id, a GUID)
is the clearest example: unlike `componenttype`/`queryType`/the systemform
`type` option set (each backed by a documented, authoritative Microsoft
reference — the SDK's own enum, or a table on a "Choices/Options"
reference page), there is no equally authoritative source enumerating
every control ever registered on a real tenant, and a wrong guess would
misrepresent real data rather than just under-describe it.

**Dataverse's own *standard* controls are the one exception, not a
contradiction of this rule** — a small, knowable set, now mapped to a
friendly name via `FormControl.Control`/`StandardFormControls` (e.g.
`SingleLineText`, `Lookup`, `Subgrid` instead of a raw GUID). What makes
this different from guessing: every entry was cross-checked against real,
live, Microsoft-published FormXML (not a docs page alone, and not a single
third-party tool's own internal table) before being trusted for a round
trip that writes back to a real form — see `StandardFormControls`'s own
doc comment for exactly which entries are confirmed that way versus
corroborated-but-not-personally-live-confirmed, and what was deliberately
left out rather than guessed (`BigInt` — no control exists, Dataverse
doesn't support it on forms at all; `UniqueIdentifier` — no control found
anywhere; Business Process Flow — its class id lives in a `workflow`
record's own Xaml, not `systemform` FormXML, so out of scope here by
construction).

A control is either one of Dataverse's own standard ones (`Control`) or
something else (`CustomControlId`) — never both, and this tool refuses to
guess which when it's ambiguous (see `FormControlValidator` below) rather
than picking one silently. **Legacy**: a `*.form.yml` hand-authored before
this split existed may still carry the older `classId` key directly —
still read, for compatibility, but never written; a fresh `form export`
always produces `control`/`customControlId` instead.

## Rebuilding FormXML (`form build-xml`)

`FormXmlWriter` (`d365architect form build-xml --input x.form.yml`) applies
every rule above in reverse to rebuild a `<form>` element from a
`FormDefinition`. Needs sign-in: before building anything, it looks the
form up live via `IFormXmlBuildService` — by the YAML's own `FormId` when
it has one (`IDataverseClient.TryGetSystemFormByIdAsync`), the same
preference `form import` already had, and for the same reason: several
forms can share a display name (confirmed live — a real table with three
forms all named "Information"), and only an id tells them apart. Table +
name (`TryGetSystemFormXmlAsync`) is the fallback, for a `*.form.yml`
exported before `FormId` existed. This was a real, reported gap, not a
hypothetical one: `build-xml` used to ignore `FormId` unconditionally even
when present, so it was the one command left unusable in exactly the
situation — duplicate names — where `FormId` matters most, forcing
reliance on `form import`'s own dry-run (which already preferred `FormId`)
as a workaround. A `FormId` that no longer resolves to anything (the form
was deleted) falls back to building fresh from just the YAML, same as a
table + name match finding nothing — never an error, consistent with this
command's own "never refuses for a missing form" design. Like `form
import`, a resolved id whose live table/name have drifted from the YAML's
own prints a warning (`ExistingSystemForm.BuildIdentityMismatchWarning`,
shared by both commands) rather than blocking — the id is still
authoritative.

**Two different modes, depending on what that lookup finds:**

- **The form already exists** (the common case — editing YAML for a form
  `form export` already produced): its current, live FormXML is the base
  document. `FormXmlWriter.Write(form, existingRoot)` only replaces the
  top-level elements this tool actually manages — `ancestor`,
  `hiddencontrols`, `tabs`, `header`/`footer`, `events`, `formLibraries`,
  `DisplayConditions`, `controlDescriptions` — each one in place if the
  document already had it, appended if not, removed if the YAML no longer
  calls for one. Everything else on that document — `Navigation`,
  `clientresources`, `RibbonDiffXml`, `formparameters`,
  `externaldependencies`, a tab's own `tabheader`/`tabfooter`, and the form
  root's own chrome attributes (`showImage`, `headerdensity`, ...) — is
  never looked at, so it survives untouched. This is the mechanism, not
  just an aspiration: every one of those was a real, documented gap in the
  from-scratch approach this tool used before; patching a live document
  closes all of them at once, for the simple reason that closing them no
  longer means modeling them — it means not touching them.
- **The form doesn't exist yet** (`TryGetSystemFormXmlAsync` returns
  null — a brand-new form this YAML describes but that hasn't been created
  in Dataverse): falls back to building a `<form>` from scratch, exactly as
  before. The "documented gap" list above still applies in full here, for
  the unavoidable reason that there's no live document to preserve those
  features from.

**Dashboards are still refused outright either way** — not just in the
from-scratch case. A dashboard's tiles (`<Visualization>`/`<SavedQuery>`)
live *inside* `<tabs>`, which this tool always replaces wholesale in both
modes, so patching a live dashboard's FormXML would delete its tiles just
as surely as building one from scratch would.

**What this still doesn't do**: write anything back into Dataverse. `form
build-xml` only ever reads (the lookup) and writes a local file — the
actual create/update call is `form import`'s job (see below), and
`build-xml` is never a required step on the way there: import does its own
independent retrieve-and-patch rather than calling into or depending on
this command in any way. `build-xml` exists for a human to inspect and
validate what would get built, on demand, not as plumbing.

**Every rebuild is validated against Microsoft's own FormXML schema**
(`FormXmlValidator`, see `Resources/FormXmlSchema/NOTICE.md` for exactly
which files and where they came from) before being written. `form
build-xml` itself only ever writes a local file, so every violation is
printed and the file gets written regardless of what it finds — there's
nothing live at stake for this command to gate on. **`form import` is
different: it refuses to import at all when a non-confirmed-safe violation
is found**, unless `--allow-schema-violations` is explicitly passed (see
below) — the two commands share the exact same `FormXmlValidator` call and
the exact same `FormXmlValidationConsole` rendering, but only one of them
actually writes to a live environment, and only that one enforces anything
off the back of what it finds.

That split exists because of a real, live-confirmed incident, not
speculatively: a violation ("the element 'parameters' has invalid child
element 'X'") was once treated the same non-blocking way as the
`headerdensity`/`showinformselector` case above, on the same "the schema
and real Dataverse output disagree sometimes" reasoning — except this one
wasn't actually safe, and a real `form import` attempt failed with a raw
Dataverse 400 (`0x80048425`, "does not conform to the required schema").
So `FormXmlValidationMessage.IsKnownHarmless` is deliberately narrow: true
only for the one specific, confirmed-safe `headerdensity`/
`showinformselector` pattern, never inferred for anything that merely looks
similar. `form import` treats every other violation as blocking by
default, and `FormXmlValidationConsole` prints a confirmed-safe one in
yellow, everything else in red, so the distinction is visible at a glance
before you ever reach `--allow-schema-violations`.

**A second, different incident showed the same gap exists the other
direction too — a missing attribute, not an extra element, and one the XSD
never checks at all.** A control with no `classid` failed live with a
different, non-schema Dataverse error (`0x80040203`, "The class id cannot
be null for control element..."): `classid` isn't declared required by the
FormXML XSD (so `FormXmlValidator` alone would never flag its absence),
but Dataverse's write-time validation requires it anyway on every real
control checked. `FormControlValidator` catches this specifically —
walking every control on the form and flagging any with no `ClassId`,
*unless* the existing live control with that same id also has none (the
same "don't block on a value nobody's actually trying to change" carve-out
as the Precision/MaxLength case above, needed here because
`FormXmlWriter` always replaces `header`/`footer`/`tabs` wholesale rather
than patching one control at a time, so an untouched classid-less control
would otherwise get flagged every time something *else* on the form
changed). `FormControlValidator` also catches two purely local mistakes
that don't need Dataverse at all to know are wrong: `control` and
`customControlId` both set on the same control (mutually exclusive — see
Rule 4), and a `control` value that isn't one of `StandardFormControls`'
recognized names (almost certainly a typo, since a real one only ever
comes from a fresh `form export` in the first place). All three findings
are `FormXmlValidationMessage`s, merged straight into the same list
`FormXmlValidator` produces — so they get the identical
`IsKnownHarmless`-gated blocking treatment, no separate mechanism needed.

Each violation (`FormXmlValidationMessage`) also carries .NET's own
`XmlSeverityType` (`Error`/`Warning`) alongside the message — checked
empirically across every violation shape seen so far (an undeclared
attribute, an invalid child element, incomplete content, an invalid choice
member): all of them come back `Error`. `Warning` is reserved for a small
set of lax-wildcard cases this schema's own structure doesn't seem to hit
anywhere this tool's output reaches, so it may never actually appear in
practice — exposed anyway since .NET is the authority on it, not a guess.
Deliberately **not** what `IsKnownHarmless` is based on, though: both the
confirmed-safe case and the confirmed-*unsafe* one that prompted this whole
split come back as the identical `Error` severity, so severity alone was
never a safe signal for whether Dataverse will actually reject the write —
only the specific, named pattern is. It also carries a `Snippet` of the
offending FormXML plus a `SnippetCaretOffset` into it, so both commands can
point at the exact spot rather than just printing a line/column pair —
deliberately an offset for the *caller* to highlight (inline, inverse
video) rather than a second line of spaces-and-a-caret baked into the
snippet itself: FormXML is always one very long line, a console can wrap
that onto several display lines, and a separate caret line's alignment
would silently break the moment that happens, while an inline highlight
travels with the character regardless.

**Record-level fields aren't FormXML's job.** `Name`, `Description`,
`Type`, `IsDefault`, `FormActivationState`, and `IsCustomizable` live on
the `systemform` record itself (`GetFormDefinitionsJsonAsync`'s own
columns), not inside the `formxml` blob — `FormXmlWriter` correctly
doesn't touch them, and a future import step would set them as separate
properties on the same create/update request that carries the rebuilt
`formxml`, not encode them into the XML somehow.

**Ids this tool never captured are synthesised deterministically.** A
tab/section/cell's own GUID, a control's `uniqueid`, a library's
`libraryUniqueId`, and a handler's `handlerUniqueId` were never round-tripped
in the first place (see the "Deliberately excluded" notes on the relevant
models) — there's no original value to restore. Rather than
`Guid.NewGuid()` on every call, `FormXmlWriter.DeterministicGuid` derives
each one from stable, human-authored data (a tab's name/label, a control's
own id, ...), the same idea as a name-based UUID (RFC 4122 §4.3). This
means re-running `build-xml` on unchanged YAML produces byte-identical
FormXML — useful for diffing, and for not making every apply look like a
change even when nothing did.

**A tab or section can genuinely have no `name` attribute at all** (a real
"Card" form's tabs, confirmed live) — only a `label`. `FormXmlWriter` never
invents one to fill the gap; the fallback used for id-seeding (falling back
to the label, then a fixed placeholder) is never written out as the actual
`name` attribute unless the source data had one. An earlier version of this
code got this wrong (synthesized `name: tab_1` into a tab that never had a
`name` at all) — caught by round-tripping every form exported this session
back through the reader afterward and diffing the result against the
original, not by inspection.

**Verified, not assumed**: every non-dashboard form exported from two
tables in a real tenant this session round-trips byte-identical
(`FormDefinition` → YAML → `FormXmlWriter.Write` → wrapped as a fake
`systemforms` response → `FormJsonDefinitionReader` again → YAML again),
except for one confirmed, harmless, and documented wrinkle — see
`FormXmlWriter`'s own doc comment on `PopulateParameterElement` for exactly
what it is and why it doesn't lose anything.

## Importing FormXML (`form import`)

`IFormImportService`/`FormImportService` (`d365architect form import
--input x.form.yml [--yes]`) writes a `*.form.yml` file's rebuilt FormXML
back into Dataverse — directly from the YAML. It does **not** call `form
build-xml` or `IFormXmlBuildService` first, and never shares a call path
with either: it does its own independent lookup and its own
`FormXmlWriter.Write(form, existingRoot)` call. This was a deliberate
design choice, not an oversight: `build-xml` is a standalone tool for a
human to inspect and validate what would get built, on demand — never
plumbing something else is required to route through.

**Looks the form up by `FormId` when the YAML has one** (`IDataverseClient.
TryGetSystemFormByIdAsync`) — the ordinary case for anything that's been
through `form export` since `FormId` was added. An id can't go stale or
ambiguous the way table + name can (a rename, or two forms sharing a name —
see `AmbiguousSystemFormException`), so once present it's what's actually
imported onto; `Entity`/`Name` become purely informational at that point.
Falls back to the old table + name lookup (`TryGetSystemFormAsync`) only
for a `*.form.yml` exported before `FormId` existed. If the id resolves to
a form whose live table/name no longer match the YAML's own `Entity`/
`Name` — copied into the wrong file, or renamed live since export — a
warning is printed before the diff; it doesn't block, since the id is
still authoritative, but it's worth a second look. A `FormId` that no
longer resolves to anything (the form was deleted) throws
`FormNotFoundException` with that id in the message, distinct from the
table + name variant of the same exception.

**Only ever updates a form that already exists.** Nothing matching throws
`FormNotFoundException` rather than creating one — creating a brand-new
form isn't supported yet (it would need a fair bit more: minimum required
systemform properties beyond just `formxml`, and likely solution-context
registration). Refuses a dashboard outright too, same as `build-xml`, for
the same reason (`FormXmlWriter` itself refuses).

**"Must have a way to check differences between client and server"** (this
feature's own originating requirement) is `TextDiff`, applied to the actual
FormXML on both sides — but not naively. Diffing the live document's *raw*
FormXML directly against the rebuild was tried first, and confirmed live
(against a real, richly-customized production form, not a synthetic test
case) to be nearly useless for this: every tab, section, and cell showed up
as "changed", purely because their wrapper ids are resynthesized fresh on
every rebuild — a wall of noise even when re-importing a file with no
meaningful edits at all.

The fix, once that was seen: rebuild the *live* form's own content through
the exact same `FormXmlWriter` call, base document, and deterministic id
rules that produced the new FormXML (`FormImportPreview.ExistingComparableFormXml` —
decompose the live FormXML the same way `form export` would, then run that
back through `FormXmlWriter.Write` against the same base document). Since
both sides are now canonicalized through the identical pipeline, unchanged
content produces identical ids and identical attribute-stripping on both
sides and disappears from the diff entirely — only a genuine difference in
the underlying `FormDefinition` content survives to show up. Both sides
are pretty-printed (one element per line) purely for this display; the
actual payload sent to Dataverse is untouched by that. Only the changed
lines plus a couple of lines of context are shown, not the whole document.
If the two sides are identical (`FormImportPreview.HasChanges` is false),
nothing is written and nothing is asked — confirmed live to actually be
true in practice for an unedited file on a real, complex form, not just in
a minimal test case.

**Be precise about what this diff does and doesn't catch**: it compares a
canonicalized rebuild of the live document against what's about to be
written, not against what the live document looked like when this YAML was
last exported. It will not by itself notice "someone else changed this
form after I exported it, before I imported my own change" — only that the
live document currently differs from the rebuild, which could be *because*
of that concurrent change just as easily as because of your own edits,
with no way from this diff alone to tell which. Catching that specifically
would need tracking a version marker (e.g. `modifiedon`, or an ETag via
`If-Match`) at export time, which this tool doesn't do yet. What the diff
*does* reliably catch: every meaningful change about to happen — which is
still the concrete, useful half of "checking differences", just not the
whole of it.

**Confirmation is the default, not an afterthought.** Unless `--yes` is
passed, `form import` shows the diff and every `FormXmlValidator` finding
(same rendering as `build-xml`, factored into `FormXmlValidationConsole` so
both commands render it identically) and asks before writing anything,
defaulting to "no" if you just press Enter — deliberately the safer
default for a command that overwrites live configuration in a real
Dataverse environment.

**Before that prompt is even reached, though: a non-confirmed-safe
violation refuses the import outright** (exit code 1, no prompt at all)
unless `--allow-schema-violations` is passed — see "Every rebuild is
validated" above for exactly which one violation is exempt from this and
why. `--yes` alone does not bypass it; skipping the confirmation prompt and
accepting a real risk of a raw Dataverse 400 are deliberately two separate
opt-ins; this actually happened via `form build-xml`/`form import`'s shared
validator being trusted as informational-only under the same reasoning as
the `headerdensity` case, which turned out only to hold for that one case.

**Publishes after writing.** `UpdateSystemFormXmlAsync` only patches the
`systemform` record's `formxml` column — Dataverse customizations still
need publishing separately before end users see the change. `form import`
does that itself: `FormImportService.ApplyAsync` follows the write with a
call to `IDataverseClient.PublishEntityAsync`, the `PublishXml` action
against the form's owning table. There's no finer-grained way to publish a
single `systemform` on its own — confirmed against Microsoft's own docs for
`PublishXmlRequest.ParameterXml`: the `<entities><entity>` node only ever
takes a whole table's logical name (the one per-record exception,
`<dashboards>`, is a different, dashboard-only node that doesn't apply
here) — so "publish the form" necessarily means publishing everything on
its table (forms, views, ribbons, attributes alike), not just the one form
that changed. `preview.Entity` carries the table this publishes: the live
one when the form was resolved by id (`ExistingSystemForm.EntityLogicalName`),
falling back to the YAML's own `Entity` otherwise — see
`FormImportPreview.Entity`'s own doc comment.

## Importing views (`view import`)

`IViewImportService`/`ViewImportService` (`d365architect view import
--input x.view.yml [--yes]`) is the `form import` counterpart for views —
same shape (preview → diff → validate/plan → confirm → apply), but
genuinely simpler, for one structural reason: a view's FetchXml/LayoutXml
are kept **verbatim** (see `ViewDefinition`'s own doc comment), never
decomposed and rebuilt through a writer. There's no id-resynthesis to
cancel out, so the diff compares `ExistingFetchXml`/`ExistingLayoutXml`
against the local YAML's own values directly — pretty-printed purely for
the display, same as form import's XML, but with no canonicalization step
needed first.

**Only three fields are ever written**: `Description`, `FetchXml`,
`LayoutXml`. `QueryType`/`IsDefault`/`IsQuickFindQuery`/`IsUserDefined`/
`IsCustomizable` are all documented on `ViewDefinition` itself as fields
applying a YAML file back doesn't change (setting the default view, for
one, needs a dedicated qualifying action, not a plain field write) — import
respects that boundary rather than attempting a guess at any of them.

**Only updates a view that already exists** — `ViewNotFoundException`
otherwise, same reasoning as `FormNotFoundException`. A local field that's
null means "don't touch this", never "clear it" — confirmed in
`IDataverseClient.UpdateSavedQueryAsync`'s own contract, and mirrored in
`ViewImportPreview.HasChanges`, which only compares a field when the local
YAML actually has a value there.

**What this doesn't do yet**: publish the change. Unlike `form import` (see
above — it now calls `PublishEntityAsync` after writing), `view import`
still only patches the `savedquery` record itself; Dataverse customizations
still need publishing separately before end users see the change.

## Importing tables (`table import`)

`ITableImportService`/`TableImportService` (`d365architect table import
--input x.table.yml [--yes]`) is the largest and riskiest of the three
import commands, for a structural reason none of the others have: a
table isn't one XML blob or one record's few fields — it's the table
itself (a handful of properties) plus an open-ended list of columns, each
of which needs its own Dataverse-defined, type-specific create/update
shape to write at all. Confirmed against Microsoft's own documented
Web API examples before writing a line of code, not guessed — getting an
attribute metadata shape wrong risks actually damaging a live table's
schema, a categorically worse failure mode than a malformed form or view.

**Update is a full-object PUT, not a partial PATCH** — confirmed directly
from Microsoft's own docs: *"You can't use the PATCH method to update data
model entities... you must use the PUT method... and be careful to include
all the existing properties that you don't intend to change."* This
applies to both a table's own metadata and each column's. `table import`
handles this the same way `FormXmlWriter` handles FormXML: fetch the
attribute/entity's full, live representation first (`GetAttributeMetadataJsonAsync`/
`GetEntityMetadataJsonAsync`), mutate only the fields this tool tracks *in
place* (`AttributeMetadataJsonBuilder.ApplyUpdateFields`), and PUT the
whole thing straight back — never a freshly-built partial object that
could silently drop something Dataverse already had on file. `MSCRM.MergeLabels:
true` is sent on every entity/attribute PUT so an edited display name
doesn't wipe out other languages' labels this tool never touched (a
documented Dataverse gotcha: that header's absence defaults to overwriting
them).

**Only these seven column types are ever created or updated**: `String`,
`Memo`, `Integer`, `BigInt`, `Decimal`, `Money`, `DateTime` — see
`AttributeMetadataJsonBuilder.SupportedTypes`. Deliberately excluded, and
why:
- **`Picklist`/`Boolean`** need an `OptionSet` definition (the actual
  choice values) — this tool doesn't capture that on export at all yet
  (see `EntityJsonDefinitionReader`'s own doc comment: it needs a separate,
  per-attribute, type-cast request Dataverse doesn't expose in bulk).
  Writing a Picklist/Boolean column without knowing its options isn't
  possible to do correctly.
- **`Lookup`/`Customer`/`Owner`** aren't creatable via this endpoint at
  all — confirmed against Microsoft's own docs: a Lookup attribute only
  comes into existence as part of creating a whole *relationship*
  (`RelationshipDefinitions`, a materially larger and different operation),
  and a Customer lookup specifically documents a dedicated
  `CreateCustomerRelationships` action instead of a plain attribute POST.
- **`Double`** has no officially documented create-body example anywhere
  Microsoft's own Web API docs could confirm one — every other type here
  is backed by a real, verbatim example; guessing at the one that isn't,
  for an operation that edits a live table's schema, isn't a risk worth
  taking.
- Anything else (`MultiSelectPicklist`, `State`, `Status`,
  `Uniqueidentifier`, `PartyList`, `File`, `Image`, `Virtual`,
  `EntityName`, `ManagedProperty`) simply hasn't been investigated.

A column of any of these excluded types still shows up in the diff and the
per-column plan (`AttributeImportAction.SkippedUnsupportedType`) — visible,
never silently dropped — it's just never written.

**Every create/update is validated before a request is ever built** —
`AttributeChangeValidator`, checked live against real invalid changes on a
real table, not just reasoned about:
- **Changing a column's `Type` after creation** — confirmed immutable by
  Microsoft's own docs ("Once a column is saved, you can't change the data
  type"). Checked *before* comparing anything else about the two sides
  (`TableImportService`'s own Type-mismatch check runs ahead of
  `AttributesMatch`), since Type isn't one of the fields this tool ever
  writes back for an existing column — without that check first, a type
  change alongside no other difference would otherwise be silently
  reported as "Unchanged" instead of the invalid, would-fail change it
  actually is.
- **Changing a column's `SchemaName` after creation** — also confirmed
  immutable, checked the same way and for the same reason.
- **Creating a column whose `SchemaName` has no customization prefix, or
  has one but contains a character Dataverse wouldn't accept anywhere else
  in the name** (e.g. `BankName`, or `new_Bank Name` with a space) —
  Dataverse requires a prefix for a custom column, and only letters,
  digits, and underscores in a schema name; this tool never invents or
  corrects one, so both are caught here rather than left for Dataverse's
  own create call to reject.
- **Creating a column whose `Name` (logical name) doesn't match what
  Dataverse will actually derive** — Dataverse creates a new attribute's
  logical name by lowercasing `SchemaName`, not from anything you send for
  `Name` directly; if the local YAML's `Name` doesn't already equal
  `SchemaName` lowercased, the column that gets created would silently have
  a different logical name than the YAML claims, and every later import
  would treat it as a brand-new column instead of recognizing it. This is
  standard, long-established Dataverse behaviour rather than something
  re-confirmed against a fresh doc citation this session.
- **Two new columns in the same local YAML claiming the same
  `SchemaName`** — a purely local, cross-attribute check (Dataverse itself
  would only reject the second create), so it lives in
  `TableImportService` rather than the per-attribute validator.
- **An invalid `RequiredLevel`** — anything other than `None`,
  `Recommended`, `ApplicationRequired`, or `SystemRequired` (Dataverse's
  own documented values) on either create or update.
- **A non-positive, or excessive, `MaxLength`** on a String/Memo column —
  must be greater than 0, and (String only) no more than 4000. Corroborated
  across multiple sources rather than a single canonical Microsoft Learn
  page stating it as an explicit ceiling the way the checks below do — kept
  as a hard check anyway since every source agrees and Dataverse would
  reject anything higher regardless, but flagged here as the one bound in
  this list that isn't a direct citation.
- **An Integer `MinValue`/`MaxValue` outside -2147483648 to 2147483647** —
  confirmed directly against Microsoft Learn
  (`IntegerAttributeMetadata.MinValue`/`MaxValue`: "Possible values are
  -2147483648 to 2147483647"). Also what keeps the `(int)` casts in
  `AttributeMetadataJsonBuilder` safe — anything outside that range is
  rejected here, before either ever reaches that cast.
- **A Decimal or Money `Precision` outside 1 to 10** — confirmed directly
  against Microsoft Learn for Decimal (`DecimalAttributeMetadata.Precision`:
  "Possible values are 1-10"); applied to Money's `Precision` too since it's
  the identical property shape on the same platform, though Money's own
  page only re-confirms the default (2), not this exact range — a
  reasonable extension, not an independently-cited one.
- **`MinValue` greater than `MaxValue`** on an Integer, Decimal, or Money
  column — a plain sanity check, not something that needed a docs lookup.

The MaxLength-ceiling and Precision-range checks above only fire when that
specific value is actually being *changed* to something new — never when
it's merely being resent unchanged as part of updating some other field on
the same column. This was found to matter, not just designed defensively:
`account`'s own live `exchangerate` column already carries `Precision: 12`,
outside Decimal's documented 1-10 range, presumably grandfathered in before
the constraint existed (or set outside the ordinary create-time check as a
system column). Blocking every future edit to `exchangerate` over a
Precision nobody's trying to change would be exactly the false positive
this validation is supposed to prevent — confirmed live, not assumed.

Any of these show up in the column plan as `AttributeImportAction.Invalid`
with the specific reason — visible, never attempted, and never left for a
cryptic Dataverse API error to explain instead.

**Some changes Dataverse allows but warns against are shown, not
blocked** — lowering `MaxLength` or `Precision` below what existing data
might already exceed. These still plan as a normal `Update`, just with a
`Warnings` entry printed alongside it, since Microsoft's own docs only
caution against this ("you shouldn't lower it if you have data... that
exceeds the lower value") rather than documenting it as a hard,
API-enforced rejection — unconfirmed either way, so this tool warns rather
than either blocking a possibly-fine change or silently allowing a
possibly-risky one.

**Never deletes a column, ever.** A column present live but absent from
the local YAML shows as `AttributeImportAction.WouldRemove` in the plan,
with an explicit reason — and that's the end of it. There's no override
flag, and no code path in `TableImportService`/`DataverseClient` that
issues a delete request at all. Deleting a column is exactly the kind of
destructive, hard-to-reverse operation this tool refuses to guess its way
into; if a column genuinely needs removing, that's a deliberate action to
take elsewhere, not a side effect of a YAML file simply not mentioning it.

**Never creates the table itself**, either — same "don't attempt the
large, different, and separately-risky operation of bringing a whole new
object into existence" boundary `form import`/`view import` already draw
around forms/views that don't exist yet.

**The diff is the table's decomposed YAML**, not a rebuilt artifact —
unlike FormXML, there's no single canonical "table document" this tool
ever reconstructs, so there's nothing to canonicalize before comparing.
Since a column's identity (its logical name) is never resynthesized the
way a form's tab/section/cell ids are, comparing the local YAML directly
against a fresh re-export produces a clean, meaningful diff without any of
the noise a raw-artifact comparison caused for forms — confirmed live
against a real, non-trivial table (`account`), not assumed. Separately from
that informational diff, an explicit **column plan** lists exactly what
will happen to each column (create/update/skipped/would-remove) — a column
can appear "different" in the diff without anything being done about it
(unsupported type, or a removal), and the plan is where that distinction
actually shows up.

**What this doesn't do yet**: publish the change. Unlike form/view
import's still-open question about whether publishing turns out to be
necessary, Microsoft's own docs are explicit here: *"When you update a
table or column definition, use the PublishXml Action before the changes
you make are applied to the model-driven applications."* `table import`
doesn't call it yet — the change is real and stored the moment `apply`
succeeds, but won't be visible in model-driven apps until published
separately.

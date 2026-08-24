# YAML conventions

This document describes design rules this tool's exported YAML (`*.table.yml`,
`*.view.yml`, `*.form.yml`) follows, and why. Two audiences:

- **Whoever builds the other direction** (`d365architect * import`, tracked
  as a future feature, not yet implemented) needs to know exactly what an
  absent field means and how a converted structure maps back to its source
  shape — that's most of what's below.
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
| `FormDisplayCondition` | `FallbackForm` | `false` | At most one form per table can be the fallback, so `false` is definitionally the common case across a table's forms as a whole, not just an observed majority |
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
  way guessing at `FormControl.ClassId`'s meaning would (see Rule 4).
- A parameter value that is itself a double-encoded XML fragment (e.g. a
  quick view control's `QuickForms`) is parsed and converted the same way,
  recursively, rather than left as an embedded string.

This was a deliberate choice over the terser but cryptic `@name`/`#text`
XML-to-JSON convention (used by tools like Newtonsoft's `XmlNodeConverter`):
every key in this tool's YAML should read as a real word, not a sigil.

**For import**: reconstructing a `<parameters>` block from this YAML means
walking it back the other way — a `attributes`/`value` pair becomes an
element with that attribute and text, everything else becomes a child
element named after its own key, and a list becomes repeated elements with
that name.

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
| A section's own `columns` attribute (sub-column count) | ✅ Captured | 4+ sections | `FormSection.Columns` |
| `ancestor` | ✅ Captured | 6 forms | `FormDefinition.Ancestor` |
| `hiddencontrols` | ✅ Captured | 7 forms | `FormDefinition.HiddenFields` |
| `DisplayConditions` (incl. `Role`/`Everyone`) | ✅ Captured | 21 forms | `FormDefinition.DisplayCondition` |
| `formLibraries` | ✅ Captured | 9 forms | `FormDefinition.Libraries` |
| `events` (form-level) | ✅ Captured | 11 forms | `FormDefinition.Events` |
| `events` nested inside a `<cell>` (field-level) | ✅ Captured | confirmed live | `FormControl.Events` — **not documented as valid there in Microsoft's own schema at all**; found only by checking real data, not the schema. See the callout below. |
| Dashboard tiles (`Visualization`/`SavedQuery`, not `<control>`) | 📝 Documented gap | every dashboard form | Not decomposed — see `FormDefinition`'s own doc comment |
| `Navigation` (related-record nav menu) | 📝 Documented gap | 11 forms | Real and non-trivial, but menu chrome rather than form content |
| `clientresources` (JS/CSS resource declarations) | 📝 Documented gap | 8 forms | Largely redundant with `events`'/`formLibraries`' own library references |
| Rare cell flags: `ischartcell`, `isstreamcell`, `istilecell` | 📝 Documented gap | 1 each | Legacy Interactive Service Hub artifacts |
| `RibbonDiffXml` (per-form command bar) | 📝 Documented gap | 0 | Its own large XML dialect; confirmed absent from every form checked, not just skipped |
| `formparameters`, `externaldependencies` | 📝 Documented gap | 0 | Confirmed absent from every form checked |
| A tab's own `tabheader`/`tabfooter` | 📝 Documented gap | 0 | Distinct from the form-level `header`/`footer` this tool already captures |
| Form root's own display attributes (`showImage`, `shownavigationbar`, `maxWidth`, `hasmargin`, `headerdensity`, `showinformselector`) | 📝 Documented gap | present on every form | Chrome/rendering settings for the form shell, not its content — same reasoning as the already-excluded `formpresentation` |

### Where the published schema itself turned out to be wrong

Three things confirmed only by checking real tenant data, not Microsoft's
schema documentation — worth knowing before trusting that schema as the last
word on anything else:

- `controlDescriptions`/`customControl`'s `id` attribute is documented
  `use="required"`; real forms have `customControl` entries with no `id` at
  all (only `name`/`formFactor`).
- A `<cell>` is documented as only ever containing `<labels>`/`<control>`;
  real forms nest a field-level `<events>` block directly inside one too
  (see the audit table above).
- A `<form>`'s root attributes documented in the schema don't include
  `headerdensity` or `showinformselector`, both of which appear on real
  forms anyway.

None of these are reasons to distrust the schema generally — it's still the
only authoritative source for the shapes this tool *does* rely on (e.g. the
boolean-with-no-default argument in Rule 3) — just a reminder to verify
against real data before treating an absence in the schema as proof
something doesn't happen in practice.

## Rule 4: raw platform identifiers are never guessed at

`AttributeDefinition`/`FormControl` keep some Dataverse identifiers as raw,
uninterpreted strings — most notably `FormControl.ClassId` (the control's
class id, a GUID). This tool deliberately does not maintain a
classid-to-friendly-name lookup table: unlike `componenttype`/`queryType`/the
systemform `type` option set (each backed by a documented, authoritative
Microsoft reference — the SDK's own enum, or a table on a "Choices/Options"
reference page), there is no equally authoritative source enumerating every
control class id, and a wrong guess would misrepresent real data rather than
just under-describe it. **For import**: `ClassId` should be written back to
FormXML verbatim, exactly as read.

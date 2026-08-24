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
  way guessing at `FormControl.ClassId`'s meaning would (see Rule 4).
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
| Cell `colspan`/`rowspan` | ✅ Captured | 5 non-default spans on one real form (up to `rowspan="15"`) | `FormControl.ColumnSpan`/`RowSpan` — genuinely structural (how much visual space a control's cell occupies), not cosmetic; found via a live round-trip check, not the initial schema audit |
| `ancestor` | ✅ Captured | 6 forms | `FormDefinition.Ancestor` |
| `hiddencontrols` | ✅ Captured | 7 forms | `FormDefinition.HiddenFields` |
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
| Tab/section-level designer/rendering attributes (`locklevel`, `IsUserDefined`, `showlabel`, `showbar`, `layout`, `celllabelalignment`, `celllabelposition`, `labelwidth`, `labelid`, `verticallayout`, `expanded`) | 📝 Documented gap | present on every tab/section | Same "chrome, not content" reasoning as the form root's own display attributes above, just at the tab/section level instead — extending that exclusion explicitly rather than leaving it implicit |
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

## Rebuilding FormXML (`form build-xml`)

`FormXmlWriter` (`d365architect form build-xml --input x.form.yml`) applies
every rule above in reverse to rebuild a `<form>` element from a
`FormDefinition`. Needs sign-in: before building anything, it calls
`IDataverseClient.TryGetSystemFormXmlAsync` (via `IFormXmlBuildService`) to
look the form up live, by table + name — the same identity `form export`
itself uses, since `formid` was never part of this tool's YAML in the first
place (see `FormDefinition`'s own doc comment).

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
actual create/update call, plus publish and any pre-flight
conflict/staleness check, is the scope of a future `form import`, which
this is one building block toward.

**Every rebuild is validated against Microsoft's own FormXML schema**
(`FormXmlValidator`, see `Resources/FormXmlSchema/NOTICE.md` for exactly
which files and where they came from) before being written, and any
violation is printed as a warning — the file still gets written either
way. This is deliberately a warning, not a gate: see "Where the published
schema itself turned out to be wrong" above for a confirmed case
(`headerdensity`/`showinformselector`) where real, live Dataverse FormXML
already fails this exact validation, through no fault of anything this
tool does. A violation is worth reading, not necessarily worth acting on.

Each violation (`FormXmlValidationMessage`) carries .NET's own
`XmlSeverityType` (`Error`/`Warning`) alongside the message — checked
empirically across every violation shape seen so far (an undeclared
attribute, an invalid child element, incomplete content, an invalid choice
member): all of them come back `Error`. `Warning` is reserved for a small
set of lax-wildcard cases this schema's own structure doesn't seem to hit
anywhere this tool's output reaches, so it may never actually appear in
practice — exposed anyway since .NET is the authority on it, not a guess,
and either severity is still just a `form build-xml` warning regardless
(see above). It also carries a `Snippet` of the offending FormXML plus a
`SnippetCaretOffset` into it, so `form build-xml` can point at the exact
spot rather than just printing a line/column pair — deliberately an offset
for the *caller* to highlight (inline, inverse video) rather than a
second line of spaces-and-a-caret baked into the snippet itself: FormXML
is always one very long line, a console can wrap that onto several display
lines, and a separate caret line's alignment would silently break the
moment that happens, while an inline highlight travels with the character
regardless.

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

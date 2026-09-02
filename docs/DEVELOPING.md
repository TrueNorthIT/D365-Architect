# Developing D365 Architect

This is the developer-facing counterpart to the root [`README.md`](../README.md):
architecture, how the pieces fit together, and the conventions this codebase
follows — for anyone working *on* the tool, not just running it. It isn't
packaged into the standalone build (only `README.md` is — see the `csproj`'s
`CopyToPublishDirectory` item); it only ever lives in the repo/source checkout.

See also [`yaml-conventions.md`](yaml-conventions.md) for the detailed,
audited rules the exported YAML itself follows (what an absent field means,
how a converted structure maps back to its source shape) — this document
covers the code around that YAML, not the YAML's own design rules.

## Architecture at a glance

| Folder | Purpose |
|---|---|
| `Program.cs` | Composition root — see below. |
| `Commands/` | One subfolder per CLI branch (`Auth`, `Environments`, `Table`, `View`, `Form`, `Schema`), each a thin Spectre.Console.Cli layer over `Services/`. |
| `Services/Authentication/` | MSAL-based sign-in, token cache, current-session tracking. |
| `Services/Dataverse/` | The Web API client and its supporting validators/exceptions/DTOs. |
| `Services/Conversion/` (+ `Models/`) | Table/view/form ↔ YAML readers, writers, and the curated models themselves. The biggest folder by far. |
| `Services/Schema/` | Generates `schema/*.schema.json` from the curated models via reflection. |
| `Services/EnvironmentService.cs` | **Placeholder scaffolding** — see below, don't mistake it for real config. |
| `Infrastructure/` | The two-class bridge wiring Spectre.Console.Cli to Microsoft.Extensions.DependencyInjection. |
| `Resources/FormXmlSchema/` | Microsoft's own FormXML XSD, vendored verbatim, embedded into the build. |
| `Properties/PublishProfiles/` | The standalone single-file publish profile(s). |
| `schema/` (repo root) | Generated JSON Schemas, committed — output of `schema export`, not hand-maintained. |
| `docs/` | This file and `yaml-conventions.md`. |
| `.github/workflows/` | CI/CD — see [Versioning & releases](#versioning--releases) below. |

## Entry point & DI wiring

`Program.cs` is top-level statements (no `Main` method), and is the entire
composition root, in three explicit steps:

1. **Register services** into a plain `ServiceCollection` — `IEnvironmentService`,
   `AuthenticationOptions.FromEnvironment()`, `IAuthenticationService` →
   `MsalAuthenticationService`, `IDataverseClient` → `DataverseClient` (via
   `services.AddHttpClient<IDataverseClient, DataverseClient>()`), and one
   reader/converter/export/import service per asset type.
2. **Bridge to Spectre.Console.Cli** via `Infrastructure/TypeRegistrar.cs` +
   `TypeResolver.cs` — a small `ITypeRegistrar`/`ITypeResolver` adapter pair so
   Spectre resolves every command (and its constructor dependencies) through
   the same DI container rather than `new`-ing them up itself.
   `TypeRegistrar.Build()` is what calls `services.BuildServiceProvider()`.
3. **Configure the command tree** — `app.Configure(config => { ... })` calls
   one `Configure(IConfigurator)` static method per feature area
   (`AuthCommands`, `EnvironmentCommands`, `TableCommands`, `ViewCommands`,
   `FormCommands`, `SchemaCommands`); `WhoAmICommand` self-registers as a
   standalone top-level command (no branch).

The doc comments in `Program.cs` are explicit about the intent: commands never
construct their own services or reach for a static/singleton instance, and
adding a new command/branch is meant to be a one-line addition to that
`Configure` call.

## Commands/

Every command derives from Spectre's `Command<TSettings>` (sync) or
`AsyncCommand<TSettings>` (async), takes its services via primary-constructor
injection, and defines a nested `Settings : CommandSettings` with
`[CommandOption("-x|--long <PLACEHOLDER>")]`/`[Description]` attributes.

| Branch | Commands | Source |
|---|---|---|
| `auth` | `login` | `Commands/Auth/LoginCommand.cs` |
| *(top-level)* | `whoami` | `Commands/WhoAmICommand.cs` |
| `environment` | `list`, `sync` | `Commands/Environments/ListEnvironmentsCommand.cs`, `SyncEnvironmentCommand.cs` |
| `table` | `export`, `import` | `Commands/Table/ExportTableCommand.cs`, `ImportTableCommand.cs` |
| `view` | `export`, `import` | `Commands/View/ExportViewCommand.cs`, `ImportViewCommand.cs` |
| `form` | `export`, `build-xml`, `import` | `Commands/Form/ExportFormCommand.cs`, `BuildFormXmlCommand.cs`, `ImportFormCommand.cs` |
| `schema` | `export`, `configure-vscode` | `Commands/Schema/ExportSchemaCommand.cs`, `ConfigureVsCodeCommand.cs` |

A few shared helpers live alongside the commands that use them rather than in
`Services/`, since they're rendering/parsing concerns specific to the CLI
layer: `Commands/DiffConsole.cs` (the shared diff renderer), `Commands/Table/EntityYamlFileReader.cs`,
`Commands/Form/FormYamlFileReader.cs` and `FormXmlValidationConsole.cs`.

### The diff-before-confirm pattern

All three import commands (`table import`, `view import`, `form import`)
share one shape, worth knowing before touching any of them:

```
PreviewAsync()                     // builds a preview: what would change, and why
  → check HasChanges / build a plan
  → print a diff via DiffConsole.PrintDiff(TextDiff.Compute(...))
  → print extra structured info    // FormXmlValidator findings, or the column plan
  → AnsiConsole.Confirm(...)        // gated by --yes, defaults to "no"
  → ApplyAsync()                   // wrapped in an AnsiConsole.Status() spinner
```

Errors are caught per-command and mapped to a plain red message
(`AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]")`) plus exit
code 1 — never a raw stack trace. `.EscapeMarkup()` matters here, not just as
style: `ex.Message` can carry raw external content this tool doesn't control
(a Dataverse HTTP error body, in particular) — an unescaped `[...]` sequence
inside it makes Spectre.Console try to parse it as markup and throw its own
unrelated `Could not find color or style '...'` error, masking whatever the
real error was. Domain-specific exceptions (`FormNotFoundException`,
`AmbiguousSystemFormException`, `AuthenticationRequiredException`, etc.) exist
specifically to give that message something meaningful to say.

## Services/

### Authentication

`MsalAuthenticationService` uses **MSAL.NET**, not a hand-rolled OAuth flow,
and deliberately **not** the device-code flow:

- **Login**: `AcquireTokenInteractive` with `Prompt.SelectAccount`, opening the
  system browser (avoids bundling an embedded webview dependency).
- **Reuse**: `AcquireTokenSilent` on every later command, throwing
  `AuthenticationRequiredException` on `MsalUiRequiredException` (expired/no
  session).
- **Token cache**: `%LocalAppData%\d365architect\msal.cache`, persisted via
  `Microsoft.Identity.Client.Extensions.Msal`'s `MsalCacheHelper` —
  OS-encrypted (DPAPI/keychain), letting a separate later CLI process reuse
  the sign-in.
- **Current session**: a separate, non-secret `%LocalAppData%\d365architect\profile.json`
  (`{ EnvironmentUrl, Username }`) tracks which environment/account is
  "current" — independent of the token cache itself.
- **Client id/authority**: `AuthenticationOptions.cs` defaults to Microsoft's
  own documented public client id for native/console Dataverse auth
  (`51f81489-12ee-4a9e-aaae-a2591f45987d`), overridable via the
  `d365architect_CLIENT_ID`/`d365architect_AUTHORITY` env vars for tenants
  whose Conditional Access policies require their own app registration.

There's no config file or committed secret anywhere for any of this — see
`README.md`'s own note that there's no `auth logout` yet either (delete the
`%LocalAppData%\d365architect` folder to sign out).

### Dataverse

`DataverseClient` is a **raw `HttpClient`** against the Web API
(`/api/data/v9.2/`) — not the Dataverse SDK. Auth isn't this class's job; a
bearer token is passed in per call. Worth knowing before extending it:

- **Entity/attribute updates are always a full-object PUT**, never a partial
  PATCH — confirmed directly from Microsoft's own docs ("you must use the PUT
  method... and be careful to include all the existing properties that you
  don't intend to change"). `MSCRM.MergeLabels: true` is sent on every such
  PUT so an edited display name doesn't wipe out other languages' labels this
  tool never touched.
- **Solution-scoping** (the `--solution` option on export commands) goes
  through `solutioncomponents`, filtered by `componenttype` (2=Attribute,
  26=View/SavedQuery, 60=SystemForm) plus `_solutionid_value`.
- **Publishing** (`PublishEntityAsync`, `PublishXml`) always publishes a whole
  table (attributes/forms/views/ribbons together) — there's no documented way
  to publish a single `systemform` on its own; `<entities><entity>` only ever
  takes a table's logical name.
- `GetEntityDefinitionJsonAsync` deliberately omits `$select` on the expanded
  `Attributes` collection, since type-specific properties (MaxLength,
  Precision, Targets, ...) aren't selectable on the base `AttributeMetadata`
  type and would 400 if named.

Supporting types: `AttributeChangeValidator` (pre-flight validation before any
create/update request is built — every numeric bound in it is annotated with
where it was confirmed, e.g. Microsoft Learn vs. "corroborated across
multiple sources, not a single canonical page"), `AttributeMetadataJsonBuilder`
(builds/mutates column JSON; `SupportedTypes` = `String`, `Memo`, `Integer`,
`BigInt`, `Decimal`, `Money`, `DateTime` only — see its own doc comment for
exactly why each excluded type is excluded), plus the not-found/ambiguous
exceptions and `Existing*`/`WhoAmIResult` DTOs.

### Conversion (the biggest folder)

The key design split, from `IEntityDefinitionReader`'s own doc comment: a
**table** needs two reader strategies — `EntityXmlDefinitionReader` (an
unpacked solution's `Entity.xml`) and `EntityJsonDefinitionReader` (live Web
API metadata) — because it can be sourced from disk or from a live
environment. **Views and forms never need that split**: their content
(`fetchxml`/`layoutxml`/`formxml`) is itself XML even when the wrapping record
comes back as JSON from the Web API, so only one JSON reader per asset type is
needed (`ViewJsonDefinitionReader`, `FormJsonDefinitionReader`).

- **Views keep FetchXml/LayoutXml verbatim** — never decomposed, since (unlike
  a table's columns) there's no bulk metadata endpoint to double-check a
  decomposition against.
- **Forms are decomposed structurally** (`FormJsonDefinitionReader` /
  `FormXmlWriter`, `tabs` → `columns` → `sections` → `controls`) because a raw
  FormXML blob isn't reviewable or diffable. `FormXmlWriter.Write(form,
  existingRoot)` patches only the elements this tool manages onto the *live*
  document rather than rebuilding a `<form>` from scratch, so anything never
  decomposed (`Navigation`, `RibbonDiffXml`, ...) survives untouched. Ids this
  tool never round-trips (a tab/cell's own GUID, a control's `uniqueid`, ...)
  are synthesized via `DeterministicGuid` — derived from stable, human-authored
  data rather than `Guid.NewGuid()` — so re-running on unchanged YAML produces
  byte-identical FormXML.
- `FormXmlValidator`/`FormControlValidator` catch two different classes of
  problem: schema violations against Microsoft's vendored XSD, and
  Dataverse's own stricter write-time requirements the XSD doesn't declare at
  all (e.g. a missing `classid`). `FormXmlValidationMessage.IsKnownHarmless`
  is deliberately narrow — see [FormXML schema validation](#formxml-schema-validation)
  below.

Shared plumbing worth knowing:

- `DefaultValueConventions.cs` — the "an absent field means Dataverse's own
  default" helpers (`RequiredLevelOrNull`, `TrueOrNull`, `FalseOrNull`) — see
  [`yaml-conventions.md`](yaml-conventions.md) for the full rule and every
  field it applies to.
- `ReadOnlyListYamlTypeConverter.cs` — every curated model exposes lists as
  `IReadOnlyList<T>` ("this is a read model, not something to mutate");
  YamlDotNet can't instantiate an interface on deserialize, so this converter
  reads into a concrete `List<T>` and hands it back through the interface.
- `TextDiff.cs` — a classic LCS line diff. Notably, `form import`'s diff is
  **not** the live document's raw FormXML compared against the rebuild — that
  was tried first and showed every tab/section/cell as "changed" purely from
  resynthesized ids. The fix: rebuild the *live* form's own content through
  the identical `FormXmlWriter`/id rules first (`FormImportPreview.
  ExistingComparableFormXml`), so only genuine content differences survive to
  the diff.
- `AssetFileNaming.cs` — `Slugify`/`MakeUnique`, used by views/forms since,
  unlike tables/columns, they have no stable logical name to file under.

`Conversion/Models/` holds the curated read models themselves
(`EntityDefinition`, `ViewDefinition`, `FormDefinition`, ...) — these are what
`YamlSchemaGenerator` (below) reflects over, and what every
`*YamlSerializer`/`*YamlDeserializer` (de)serializes.

### Schema

`YamlSchemaGenerator` reflects over a curated model type to emit a JSON
Schema, rather than hand-maintaining a second copy that could drift:

- Property names come from `[YamlMember(Alias=...)]` (or camelCase fallback) —
  the same source the matching `*YamlSerializer` uses.
- `"required"` comes from the C# `required` modifier.
- **Descriptions come from this assembly's own generated XML doc file**
  (`AppContext.BaseDirectory/{AssemblyName}.xml`) — literally the same
  `<summary>` text a developer reading the source sees. This is why
  `schema export` must be run from an ordinary `dotnet build`/`dotnet run`
  output, not the standalone single-file `.exe` (which doesn't carry the
  `.xml` doc file) — see the README's own note on this.
- `[SchemaEnum(typeof(X), nameof(X.SomeStaticMember))]` constrains a string
  property to a fixed value set (e.g. `FormControl.Control` →
  `StandardFormControls.FriendlyNames`), read via reflection so the schema's
  enum can never drift from what validation actually accepts.

**Regenerate `schema/*.schema.json` whenever a curated model's shape or doc
comments change** — those three files are committed, not built by CI, and
`schema export`'s own doc comment describes them as "re-run for all three
whenever a model changes." (One already-generated description is out of date
as of this writing — `schema/table.schema.json` still says table import is
"not yet supported" — worth a fresh `schema export --for table` next time
you're touching that model.)

### `EnvironmentService`/`IEnvironmentService` — not real yet

Both are explicitly labeled placeholder/example code in their own doc
comments: `IEnvironmentService.cs` calls itself "an example service... showing
how sibling sub-commands can depend on the same injected service," and
`EnvironmentService.cs` hardcodes `["dev","test","prod"]` and does
`Task.Delay(200)` as a stand-in for real sync logic. **This is unrelated
scaffolding, not a real config layer** — actual environment URLs are passed
directly as CLI arguments (`-u|--url` on `auth login`), and the *current*
signed-in environment is tracked by `profile.json` (see Authentication
above), not by this service. Don't build on top of `IEnvironmentService`
assuming it's wired into real Dataverse lookups — it isn't yet.

## FormXML schema validation

`Resources/FormXmlSchema/` vendors Microsoft's own official FormXML XSD
(`FormXml.xsd` + its `RibbonCore`/`RibbonTypes`/`RibbonWSS` includes) verbatim
from Microsoft's downloadable "Schemas.zip" — see `NOTICE.md` for exact
provenance. It's embedded (`<EmbeddedResource Include="Resources/FormXmlSchema/*.xsd" />`),
not just copied to the output directory, so the standalone single-file build
still has it available.

`FormXmlValidator` validates rebuilt FormXML against this schema before
`form build-xml`/`form import` write anything. The two commands treat a
violation differently:

- `form build-xml` only ever writes a **local file** — every violation is
  printed, but nothing blocks the write, since there's nothing live at stake.
- `form import` writes to a **live environment** — only one specific,
  confirmed-safe violation pattern (`headerdensity`/`showinformselector`
  attributes real Dataverse FormXML carries that the XSD doesn't declare) is
  treated as informational. Every other violation blocks the import outright
  (before the confirmation prompt) unless `--allow-schema-violations` is
  passed.

That split exists because of a real incident, not caution for its own sake: a
*different* violation was once assumed similarly harmless and a real
`form import` attempt failed live with a raw Dataverse 400. See
[`yaml-conventions.md`](yaml-conventions.md#rebuilding-formxml-form-build-xml)
for the full incident history and exactly which pattern is exempt. If you're
ever tempted to widen `FormXmlValidationMessage.IsKnownHarmless`, that history
is the reason to be very sure first — confirm against a real write, not just
against the schema.

## YAML conventions

The rules the exported/imported YAML itself follows — what an absent field
means, how a form control's `<parameters>` maps to YAML and back, why raw
platform identifiers are never guessed at — are documented in full, with
their empirical justification, in [`yaml-conventions.md`](yaml-conventions.md).
That document is written for exactly two audiences: whoever's extending the
import/export direction, and anyone hand-editing the YAML — read it before
changing any reader/writer in `Services/Conversion/`.

## Build & the dev inner loop

```
dotnet build "D365 Architect/D365 Architect.csproj" -c Release
dotnet run --project "D365 Architect" -- <command> [options]
```

Both use the framework-dependent output — the one `schema export` needs (see
above), and the faster loop for everyday changes. The standalone,
single-file `win-x64` build (`dotnet publish ... -p:PublishProfile=win-x64`)
is what CI publishes as a release asset; see the root README for the full
command and what it produces.

## Versioning & releases

Versioning comes from **[MinVer](https://github.com/adamralph/minver)**
(csproj `PackageReference`), not a hand-maintained version field:
`MinVerTagPrefix` is set to `v`, so an annotated `v1.2.3` git tag on `main`
yields a clean release version, and any other commit auto-increments the
patch and appends a `-alpha.0.{commits since tag}` pre-release suffix.

**Two things depend on this that are easy to get wrong if you're touching a
model's version-sensitive output**:
- The exe's own `AssemblyVersion`/`FileVersion`/`AssemblyInformationalVersion`
  come from MinVer at build time — nothing to maintain by hand.
- `dotnet build -getProperty:Version` **silently reports the SDK's fallback
  `1.0.0` instead of the real MinVer version unless you also pass `-t:Build`**
  — bare `-getProperty` skips target execution entirely, so MinVer's own
  version-computing task never runs. This bit CI during development (see the
  comment in `publish-release.yml`) — worth knowing if you ever script
  against the computed version yourself.

`.github/workflows/publish-release.yml` builds and publishes the standalone
win-x64 exe as a GitHub Release on every push to `main` (a real release —
either a maintainer's own pushed `vX.Y.Z` tag, or an auto-tagged patch bump)
and `develop` (always a pre-release, tagged with the raw MinVer version
string and marked "Pre-release" on GitHub). It also accepts
`workflow_dispatch` for a manual re-run against any branch (once the workflow
file itself has reached `main` — GitHub won't let you dispatch a workflow
that doesn't exist yet on the default branch).

## Testing

**There are no automated tests in this repo yet** — no test project, no
xunit/nunit/mstest references. In their place, the doc comments throughout
`Services/Conversion/` lean heavily on documented, empirical verification —
phrases like "confirmed live against a real tenant," specific
occurrence counts across real exported forms, and named incident histories
(a violation once assumed harmless that actually failed live) stand in for
what a test suite would otherwise assert. When you change behavior in
`Services/Conversion/` or `Services/Dataverse/`, the existing bar is to verify
against a real environment and document what you checked in the same style —
not just reason about it — before merging. This is a real gap worth closing
with actual tests over time, not a deliberate design choice to leave uncovered.

## Conventions to follow

- **`Nullable`/`ImplicitUsings` are both enabled**, and
  `GenerateDocumentationFile` is `true` with `CS1591` suppressed — doc
  comments aren't just decoration here, they feed `YamlSchemaGenerator`'s
  output directly. Write them as design rationale (why, with evidence), the
  way the existing ones read, not as a restatement of the signature.
- **Errors are domain-specific exceptions**, caught per-command and rendered
  as a plain red message — never let a raw exception/stack trace reach the
  console.
- **Never guess at a Dataverse API shape or bound** — every validation rule in
  `AttributeChangeValidator`/`AttributeMetadataJsonBuilder` cites where it was
  confirmed (a specific Microsoft Learn page, or "corroborated but not
  independently cited" when that's genuinely the best available). If you add
  a new check, cite it the same way, or flag explicitly that you didn't.
- **YAML keys read as real words**, never source-format sigils (`@attr`/
  `#text`) — see [`yaml-conventions.md`](yaml-conventions.md) Rule 2.
- **An absent YAML field always means "left at Dataverse's own default"** —
  never silently repurpose omission to mean something else; use
  `DefaultValueConventions.cs`'s existing helpers, or add a new one following
  the same confirmed-not-guessed standard, if you need another one.
- **Destructive operations don't get silently automated**: `table import`
  never deletes a column, `form import`/`view import` never create a
  record that doesn't already exist. If you're adding a new import path,
  keep that bar — surface what *would* change and let the user opt in,
  rather than making the tool more automatically destructive by default.

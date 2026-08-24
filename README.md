# D365 Architect CLI

A command-line tool for managing Dynamics 365 environments.

> **Status: early preview.** The commands below are the ones available right
> now. They're a starting point — expect more D365-specific functionality to
> land over time.

## Requirements

- [.NET 10 runtime](https://dotnet.microsoft.com/download) (or later) installed on your machine.

## Getting the tool

This tool isn't published as a ready-made download yet, so for now you build
it from source. Two ways to do that, depending on whether the machine you're
running it on has the .NET runtime installed:

**Framework-dependent** (needs the [.NET 10 runtime](#requirements) already
installed wherever you run it, but is the faster everyday build):

```
dotnet build "D365 Architect/D365 Architect.csproj" -c Release
```

The tool is produced as `d365architect.exe` under
`D365 Architect/bin/Release/net10.0/`, alongside its dependency DLLs — copy
the whole folder if you're moving it elsewhere, not just the `.exe`.

If you have the .NET SDK installed but just want to try it without building
a copy of anything, you can also run it directly from that folder with:

```
dotnet run --project "D365 Architect" -- <command> [options]
```

**Standalone** (a single `d365architect.exe`, with the .NET runtime and
every dependency bundled in — nothing else needs to be installed on the
machine you run it on):

```
dotnet publish "D365 Architect/D365 Architect.csproj" -c Release -p:PublishProfile=win-x64
```

Produces `d365architect.exe` under
`D365 Architect/bin/Release/net10.0/publish/win-x64/` — that one file is
everything; copy just it wherever you like. It's larger (~75 MB, since the
whole runtime travels with it) and doesn't need the `.xml`/`.pdb` sitting
next to it for normal use — those two are only for `schema export`'s
descriptions and crash symbols respectively, both dev-time concerns (see
`schema`, below). The `win-x64` profile lives at
`D365 Architect/Properties/PublishProfiles/win-x64.pubxml`; add another
`.pubxml` alongside it (with a different `RuntimeIdentifier` and
`PublishDir`) for another platform, e.g. `linux-x64` or `osx-arm64`.

## Usage

```
d365architect <command> [options]
```

Run `d365architect --help` at any time to see the commands available in your
installed version. Add `--help` after any command to see that command's
own options.

## Commands

| Command       | Description                                                  |
|---------------|---------------------------------------------------------------|
| `auth`        | Sign in to a D365 environment.                                |
| `whoami`      | Shows who you're currently signed in as, and to which environment. |
| `environment` | Manage D365 environments.                                     |
| `table`       | Work with D365 table definitions.                              |
| `view`        | Work with D365 view (saved query) definitions.                 |
| `form`        | Work with D365 form definitions.                               |
| `schema`      | Work with this tool's YAML schema.                             |

### `auth`

Sign in to a D365 environment.

#### `auth login`

Signs you in interactively: this opens your default browser to the
Microsoft sign-in page, and remembers the sign-in afterwards so you don't
need to repeat it for every command.

```
d365architect auth login --url https://yourorg.crm.dynamics.com
```

| Option        | Description                                                        | Required |
|----------------|--------------------------------------------------------------------|----------|
| `-u, --url`   | The URL of the D365 environment to sign in to                      | Yes      |

The first time you sign in to a new tenant, Microsoft may also ask you (or
your admin, if your organisation restricts this) to approve this tool's
access. Your sign-in is stored on your own machine only, under
`%LOCALAPPDATA%\d365architect`. There's no `auth logout` yet — to sign out,
delete that folder.

### `whoami`

Shows who you're currently signed in as, and to which environment. Run
`auth login` first if you see a "not signed in" message.

```
d365architect whoami
```

### `environment`

Manage D365 environments.

#### `environment list`

Lists the environments the tool currently knows about.

```
d365architect environment list
```

#### `environment sync`

Synchronises a named environment.

```
d365architect environment sync --environment dev
```

| Option              | Description                              | Required |
|---------------------|-------------------------------------------|----------|
| `-e, --environment` | Name of the environment to synchronise    | Yes      |

### `table`

Work with D365 table definitions.

#### `table export`

Fetches a table's live definition from the currently signed-in environment
and saves it as this tool's declarative YAML. Requires `auth login` first.

```
d365architect table export --table account
```

By default this exports the table's full, merged definition — every column
regardless of which solution (if any) added it. Pass `--solution` to scope
the export down to just the columns that solution actually customizes:

```
d365architect table export --table account --solution examplesolution
```

| Option            | Description                                                                | Required |
|-------------------|------------------------------------------------------------------------------|----------|
| `-t, --table`     | Logical name of the table to export, e.g. `account`                          | Yes      |
| `-s, --solution`  | Unique name of a solution to scope the export to (only that solution's columns) | No   |
| `-o, --output`    | Path to write the YAML to. Defaults to `<table>.table.yml` in the current directory | No |

Exported files follow a `<name>.<asset type>.yml` naming convention (e.g.
`account.table.yml`) so a folder of exports stays sortable and unambiguous
across tables, views, forms, and any asset type that joins them later.

### `view`

Work with D365 view (saved query) definitions.

#### `view export`

Fetches every view defined on a table from the currently signed-in
environment and saves each one as its own YAML file. Requires `auth login`
first.

```
d365architect view export --table account
```

By default this exports every view on the table — public views, Quick Find,
lookup views, associated views, and so on. Pass `--solution` to scope the
export down to just the views that solution actually customizes:

```
d365architect view export --table account --solution examplesolution
```

| Option            | Description                                                                | Required |
|-------------------|------------------------------------------------------------------------------|----------|
| `-t, --table`     | Logical name of the table whose views to export, e.g. `account`              | Yes      |
| `-s, --solution`  | Unique name of a solution to scope the export to (only that solution's views)  | No   |
| `-o, --output`    | Directory to write the exported YAML files into. Defaults to the current directory | No |

Each view is written as `<view-name>.view.yml`, following the same
`<name>.<asset type>.yml` convention as `table export` — e.g. "Active
Accounts" becomes `active-accounts.view.yml`. A view's FetchXML and
LayoutXML are kept verbatim rather than decomposed into a friendlier shape,
since (unlike a table's columns) there's no bulk metadata endpoint to
double-check that decomposition against.

### `form`

Work with D365 form definitions.

#### `form export`

Fetches one form from a table in the currently signed-in environment and
saves it as YAML. Requires `auth login` first.

```
d365architect form export --table account
```

Without `--form-id`, this looks up every form on the table (id, name, type
only — not each one's full FormXml) and prompts you to choose one
interactively, arrow keys + Enter:

```
? Select a form to export from account:
> Account Main Form
  Account Quick Create
  Account Card Form
  Account Quick View Form
(Move up/down to reveal more forms)
```

Pass `--form-id` to skip the prompt and export a specific form directly —
useful for scripting, or once you already know which form you want:

```
d365architect form export --table account --form-id 00000000-0000-0000-0000-000000000000
```

Pass `--solution` to scope the interactive list (or validate `--form-id`
against) just the forms that solution actually customizes:

```
d365architect form export --table account --solution examplesolution
```

| Option            | Description                                                                | Required |
|-------------------|------------------------------------------------------------------------------|----------|
| `-t, --table`     | Logical name of the table whose forms to choose from, e.g. `account`         | Yes      |
| `-f, --form-id`   | Id of the form to export. Omit to choose interactively from a list           | No       |
| `-s, --solution`  | Unique name of a solution to scope the list (or the form id check) to        | No       |
| `-o, --output`    | Directory to write the exported YAML file into. Defaults to the current directory | No |

The form is written as `<form-name>.form.yml`, following the same
`<name>.<asset type>.yml` convention as `table export`/`view export` — e.g.
"Account Main Form" becomes `account-main-form.form.yml`.

Unlike a view's FetchXML/LayoutXML, a form's FormXML isn't kept verbatim —
it's decomposed into `tabs` → `columns` → `sections` → `controls` (plus
`headerControls`/`footerControls` for anything pinned outside a tab). Both
levels of column layout are kept, not flattened away: a tab's own
side-by-side `columns` (each with its width), and a section's own internal
column count when it lays its fields out into more than one — a real
distinction, since two sections in different tab-columns render next to
each other rather than stacked, and a section's own multi-column layout
changes how its fields group visually even though the field list itself is
still just one flat, row-major list either way. Each control lists its id,
the attribute it's bound to (when it's bound to one), its label, and its
raw control class id. A raw XML blob of a form's layout markup isn't
something you can usefully review, diff, or drive a bulk change from; this
structure is. Every control is captured this way, not just simple fields —
a subgrid's target table/relationship/view, a web resource's name, a quick
view control's source form, and so on all show up structurally under that
control's `parameters`, converted from XML to nested YAML using the XML's
own element names rather than modeled property-by-property per control
type (FormXML has well over a dozen of them, each with its own shape). A
control can also carry `additionalControls` — alternates attached via the
form designer's "add a component" feature (a Calendar control added to a
subgrid, or per-client Web/Phone/Tablet replacements) — captured from
FormXML's separate `controlDescriptions` section rather than missed
entirely just because it lives outside the usual cell/control tree. A
control can also carry its own `events` (field-level "on change" logic),
and `visible: false` on a control marks a field that's deliberately hidden
rather than simply absent from the form.

Beyond a control's own content, the form itself carries `ancestor` (the
form it's derived from), `hiddenFields` (fields tracked but never rendered
on any tab), `displayCondition` (when this form is offered as a choice, and
to which security roles), `libraries` (JavaScript files it loads), and
`events` (its form-wide OnLoad/OnSave-style business logic) — none of
these are layout, but they're real, common, and previously invisible
otherwise. See [`docs/yaml-conventions.md`](docs/yaml-conventions.md) for
the full audit of what's captured against Microsoft's own FormXML schema,
including what's deliberately still out of scope and why. The one real
content gap is dashboards: their tiles are chart/visualization elements
rather than `<control>` elements at all, so a dashboard form's
tabs/columns/sections come back with no controls in them.

#### `form build-xml`

Rebuilds FormXML from a `*.form.yml` file — the reverse of `form export`'s
decomposition. Needs sign-in: it looks up the form's current, live FormXML
first (by table + name, the same identity `form export` uses) and patches
only the elements this tool manages onto that document, rather than
building a new `<form>` from scratch — so anything this tool has never
decomposed (`Navigation`, `clientresources`, `RibbonDiffXml`, root chrome
attributes, ...) survives untouched because it's never modified, not
because this tool reconstructed it. When no form by that name exists yet
(a brand-new form this YAML describes but hasn't been created in Dataverse),
it falls back to building fresh from just the YAML instead. This is one
building block toward a future `form import`, which would actually write
the result back — this command only ever reads.

```
d365architect form build-xml --input account-main-form.form.yml
```

| Option          | Description                                            | Required |
|-----------------|----------------------------------------------------------|----------|
| `-i, --input`   | Path to the `*.form.yml` file to rebuild FormXML from     | Yes      |
| `-o, --output`  | Path to write the rebuilt FormXML to. Defaults to `<input>.xml` | No |

This refuses to run on a dashboard-type form rather than silently produce
one with none of its visualizations — patching can't protect a dashboard's
tiles either, since they live inside `<tabs>`, which this tool always
replaces wholesale. For a genuinely new form with no live counterpart yet,
it's safe to treat the output as complete for a form built entirely from
this tool's own YAML, but the same gap list as before still applies in that
case: anything this tool documents as a deliberate gap simply won't be in
the output, since there's no prior document to have carried it. See
[`docs/yaml-conventions.md`](docs/yaml-conventions.md#rebuilding-formxml-form-build-xml)
for the full detail, including how ids this tool never captured (a
tab/section/cell's own GUID, a control's `uniqueid`, ...) are derived
deterministically so re-running this on unchanged YAML produces
byte-identical output rather than a spurious diff every time.

Before writing, the rebuilt FormXML is checked against Microsoft's own
official FormXML XSD schema (vendored in this repo — see
`D365 Architect/Resources/FormXmlSchema/NOTICE.md`); any violation is
printed as a warning but doesn't stop the file from being written, since a
violation isn't necessarily this tool's mistake — real, live Dataverse
FormXML is confirmed to violate this same schema in at least one way
(`headerdensity`/`showinformselector`), unrelated to anything `form
build-xml` does.

### `schema`

Work with this tool's YAML schema.

#### `schema export`

Writes a [JSON Schema](https://json-schema.org/) describing one of this
tool's curated YAML shapes to disk — generated directly from this tool's own
model (property names, required fields, and descriptions all come straight
from the source), so it can't drift out of sync the way a hand-written copy
could. No sign-in needed.

```
d365architect schema export --for table
d365architect schema export --for view
d365architect schema export --for form
```

| Option         | Description                                  | Required |
|----------------|------------------------------------------------|----------|
| `-f, --for`    | Which asset type to generate a schema for: `table`, `view`, or `form`. Defaults to `table` | No |
| `-o, --output` | Path to write the schema to. Defaults to `schema/<asset-type>.schema.json` | No |

A dev-time-only detail: descriptions come from this assembly's own
`d365architect.xml` doc-comments file, which sits next to `d365architect.dll`/`.exe`
in an ordinary build but isn't part of the [standalone single-file
build](#getting-the-tool) above. Run this command from a regular
`dotnet build`/`dotnet run` output (which is what maintaining this repo
already does), not the standalone `.exe`, or the generated schema's
per-field descriptions will be blank — every other command is unaffected.

This repository commits its generated schemas at
[`schema/table.schema.json`](schema/table.schema.json),
[`schema/view.schema.json`](schema/view.schema.json), and
[`schema/form.schema.json`](schema/form.schema.json), and re-runs `schema
export` for all three whenever a model changes. D365 developers can point
their editor at them for inline validation and autocomplete while
hand-editing or reviewing `*.table.yml`/`*.view.yml`/`*.form.yml` files:

- **If you have this repo cloned**, it's already wired up — `.vscode/settings.json`
  maps `*.table.yml`, `*.view.yml`, and `*.form.yml` files to their local
  schemas, so VS Code's
  [YAML extension](https://marketplace.visualstudio.com/items?itemName=redhat.vscode-yaml)
  validates them automatically.
- **From anywhere else**, add a workspace setting pointing at the raw files on
  GitHub:

  ```json
  {
    "yaml.schemas": {
      "https://raw.githubusercontent.com/TrueNorthIT/D365-Architect/main/schema/table.schema.json": "*.table.yml",
      "https://raw.githubusercontent.com/TrueNorthIT/D365-Architect/main/schema/view.schema.json": "*.view.yml",
      "https://raw.githubusercontent.com/TrueNorthIT/D365-Architect/main/schema/form.schema.json": "*.form.yml"
    }
  }
  ```

  or reference it per-file with a leading comment:

  ```yaml
  # yaml-language-server: $schema=https://raw.githubusercontent.com/TrueNorthIT/D365-Architect/main/schema/table.schema.json
  entity: account
  ```

  This works in any editor that speaks the `yaml-language-server` protocol
  (not just VS Code), fetched over plain HTTPS with no clone, no CLI, and no
  auth needed — the repository is public.

  Which branch to point at depends on how current you want to be:

  | Branch    | Use for                                                        |
  |-----------|------------------------------------------------------------------|
  | `main`    | The latest stable release.                                     |
  | `develop` | Pre-release — schema changes that have landed ahead of the next release. |

  Swap `main` for `develop` in either URL above to track pre-release schema
  changes as they land. Either way, pointing at a branch means the schema
  can change under you whenever that branch does; pin the URL to a tag or
  commit SHA instead if you want it to stay fixed.

## Getting help

- Add `--help` to any command to see its full list of options.
- Found a problem, or have a suggestion? Open an issue in this repository.

## Contributing

See [`docs/yaml-conventions.md`](docs/yaml-conventions.md) for the design
rules the exported YAML follows — what an absent field means, and how a
converted structure (e.g. a form control's parameters) maps back to its
source shape. That's written for whoever builds the eventual import
direction, not end users of the CLI.

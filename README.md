# D365 Architect CLI

A command-line tool for managing Dynamics 365 environments.

> **Status: early preview.** The commands below are the ones available right
> now. They're a starting point — expect more D365-specific functionality to
> land over time.

## Requirements

- [.NET 10 runtime](https://dotnet.microsoft.com/download) (or later) installed on your machine.

## Getting the tool

This tool isn't published as a ready-made download yet, so for now you build
it from source:

1. Get a copy of this repository.
2. From the `D365 Architect` folder, run:

   ```
   dotnet build -c Release
   ```

3. The tool is produced as `d365architect.exe` (Windows) under
   `D365 Architect\bin\Release\net10.0\`. Copy it (and the rest of that
   folder's contents) wherever you'd like to run it from.

If you have the .NET SDK installed but just want to try it without building
a standalone copy, you can also run it directly from that folder with:

```
dotnet run -- <command> [options]
```

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
once forms, views, and other asset types are supported.

### `schema`

Work with this tool's YAML schema.

#### `schema export`

Writes a [JSON Schema](https://json-schema.org/) describing the table YAML
shape to disk — generated directly from this tool's own model (property
names, required fields, and descriptions all come straight from the source),
so it can't drift out of sync the way a hand-written copy could. No sign-in
needed.

```
d365architect schema export
```

| Option         | Description                                  | Required |
|----------------|------------------------------------------------|----------|
| `-o, --output` | Path to write the schema to. Defaults to `schema/table.schema.json` | No |

This repository commits its generated schema at
[`schema/table.schema.json`](schema/table.schema.json) and re-runs `schema
export` whenever the model changes. D365 developers can point their editor
at it for inline validation and autocomplete while hand-editing or reviewing
`*.table.yml` files:

- **If you have this repo cloned**, it's already wired up — `.vscode/settings.json`
  maps `*.table.yml` files to the local schema, so VS Code's
  [YAML extension](https://marketplace.visualstudio.com/items?itemName=redhat.vscode-yaml)
  validates them automatically.
- **From anywhere else**, add a workspace setting pointing at the raw file on
  GitHub:

  ```json
  {
    "yaml.schemas": {
      "https://raw.githubusercontent.com/TrueNorthIT/D365-Architect/main/schema/table.schema.json": "*.table.yml"
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

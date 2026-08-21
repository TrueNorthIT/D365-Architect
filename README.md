# Declarative D365 CLI

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
2. From the `Declarative D365` folder, run:

   ```
   dotnet build -c Release
   ```

3. The tool is produced as `d365cli.exe` (Windows) under
   `Declarative D365\bin\Release\net10.0\`. Copy it (and the rest of that
   folder's contents) wherever you'd like to run it from.

If you have the .NET SDK installed but just want to try it without building
a standalone copy, you can also run it directly from that folder with:

```
dotnet run -- <command> [options]
```

## Usage

```
d365cli <command> [options]
```

Run `d365cli --help` at any time to see the commands available in your
installed version. Add `--help` after any command to see that command's
own options.

## Commands

| Command       | Description                                                  |
|---------------|---------------------------------------------------------------|
| `auth`        | Sign in to a D365 environment.                                |
| `whoami`      | Shows who you're currently signed in as, and to which environment. |
| `environment` | Manage D365 environments.                                     |

### `auth`

Sign in to a D365 environment.

#### `auth login`

Signs you in interactively: this opens your default browser to the
Microsoft sign-in page, and remembers the sign-in afterwards so you don't
need to repeat it for every command.

```
d365cli auth login --url https://yourorg.crm.dynamics.com
```

| Option        | Description                                                        | Required |
|----------------|--------------------------------------------------------------------|----------|
| `-u, --url`   | The URL of the D365 environment to sign in to                      | Yes      |

The first time you sign in to a new tenant, Microsoft may also ask you (or
your admin, if your organisation restricts this) to approve this tool's
access. Your sign-in is stored on your own machine only, under
`%LOCALAPPDATA%\d365cli`. There's no `auth logout` yet — to sign out,
delete that folder.

### `whoami`

Shows who you're currently signed in as, and to which environment. Run
`auth login` first if you see a "not signed in" message.

```
d365cli whoami
```

### `environment`

Manage D365 environments.

#### `environment list`

Lists the environments the tool currently knows about.

```
d365cli environment list
```

#### `environment sync`

Synchronises a named environment.

```
d365cli environment sync --environment dev
```

| Option              | Description                              | Required |
|---------------------|-------------------------------------------|----------|
| `-e, --environment` | Name of the environment to synchronise    | Yes      |

## Getting help

- Add `--help` to any command to see its full list of options.
- Found a problem, or have a suggestion? Open an issue in this repository.

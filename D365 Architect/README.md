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

### `greet`

Prints a friendly greeting — a quick way to confirm the tool is installed
and working.

```
d365architect greet --name "Ada"
```

| Option         | Description        | Default |
|----------------|---------------------|---------|
| `-n, --name`   | The name to greet   | `World` |

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

## Getting help

- Add `--help` to any command to see its full list of options.
- Found a problem, or have a suggestion? Open an issue in this repository.

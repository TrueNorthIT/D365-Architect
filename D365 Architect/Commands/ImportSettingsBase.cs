using System.ComponentModel;
using Spectre.Console.Cli;

namespace D365Architect.Commands;

/// <summary>
/// Options shared by every `import` command (`form import`, `table import`,
/// `view import`) — see <see cref="ImportRunner"/> for the flow these
/// drive. Spectre.Console.Cli discovers `[CommandOption]`
/// properties across the whole type hierarchy, so a concrete command's own
/// `Settings : ImportSettingsBase` inherits these three for free and only
/// declares whatever else is specific to it (e.g. form import's
/// `--allow-schema-violations`).
/// </summary>
public abstract class ImportSettingsBase : CommandSettings
{
    [CommandOption("-i|--input <PATH>")]
    [Description("Path to the YAML file to import.")]
    public required string Input { get; init; }

    [CommandOption("-y|--yes")]
    [Description("Skip the confirmation prompt and import immediately.")]
    public bool Yes { get; init; }

    [CommandOption("--whatif")]
    [Description("Only show the diff/plan — never prompt and never write anything to Dataverse.")]
    public bool WhatIf { get; init; }
}

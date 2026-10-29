using System.ComponentModel;
using Spectre.Console.Cli;

namespace D365Architect.Commands;

/// <summary>
/// Settings for the four commands that accept <c>--publish</c> (REQ-34):
/// <c>table create</c>, <c>table import</c>, <c>view import</c> and
/// <c>choice import</c>. Form import (which always publishes) and
/// <c>solution import</c> deliberately don't extend this (REQ-40).
/// </summary>
public abstract class PublishableImportSettings : ImportSettingsBase
{
    [CommandOption("--publish")]
    [Description("Only publish what the file describes — write nothing else. Refuses (exit 1) if it doesn't exist yet or the file has changes not yet imported; run without --publish first. Asks first unless --yes; --whatif shows what would be published.")]
    public bool Publish { get; init; }
}

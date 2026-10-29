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
    // STUB (tests written first): no [CommandOption] yet, so `--publish` isn't
    // registered on the command line and the "offers --publish" tests fail.
    // The developer adds [CommandOption("--publish")] and [Description(...)].
    public bool Publish { get; init; }
}

using System.ComponentModel;
using Spectre.Console.Cli;

namespace D365Architect.Commands;

/// <summary>
/// Settings for the commands that publish what they change once it's applied:
/// <c>table create</c>, <c>table import</c>, <c>view import</c>,
/// <c>choice import</c> and <c>form import</c> (see <see cref="ImportRunner"/>).
/// <c>solution import</c> has its own <c>--no-publish</c>, which it forwards
/// to each of these.
/// </summary>
public abstract class PublishableImportSettings : ImportSettingsBase
{
    [CommandOption("--no-publish")]
    [Description("Don't publish after importing — the change is written but end users won't see it until it's published (e.g. in the maker portal).")]
    public bool NoPublish { get; init; }
}

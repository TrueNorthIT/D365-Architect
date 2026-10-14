using D365Architect.Services.Conversion;
using Spectre.Console;

namespace D365Architect.Commands.Table;

/// <summary>
/// Renders <see cref="TablePropertyNotApplied"/> entries as the same
/// "not applied: reason" wording skipped columns get in the column plan.
/// Kept apart from <see cref="ImportTableCommand"/> so it can be tested
/// without a console.
/// </summary>
internal static class TablePropertyNotAppliedFormatter
{
    /// <summary>One Spectre markup line per property, e.g. <c>  ? hasNotes (not applied: ...)</c>.</summary>
    public static IEnumerable<string> FormatLines(IEnumerable<TablePropertyNotApplied> properties) =>
        properties.Select(p => $"[grey]  ? {p.Property.EscapeMarkup()} (not applied: {p.Reason.EscapeMarkup()})[/]");
}

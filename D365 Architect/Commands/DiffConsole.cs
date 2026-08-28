using System.Xml.Linq;
using D365Architect.Services.Conversion;
using Spectre.Console;

namespace D365Architect.Commands;

/// <summary>
/// Renders a <see cref="TextDiff"/> result to the console — shared by every
/// <c>import</c> command's confirmation step (<c>form import</c>,
/// <c>view import</c>, and <c>table import</c>), so they all show the same
/// diff style rather than each reinventing it.
/// </summary>
internal static class DiffConsole
{
    /// <summary>
    /// One element per line, indented — an XML document written compactly
    /// on one long line (as this tool's own writers do) is correct for the
    /// actual payload but useless for a line-based diff: comparing one
    /// giant line against another only ever says "line 1 changed".
    /// Re-parsing and pretty-printing purely for this display doesn't touch
    /// whatever payload actually gets sent to Dataverse.
    /// </summary>
    public static string PrettyPrintXml(string xml) => XElement.Parse(xml).ToString(SaveOptions.None);

    /// <summary>
    /// Prints only the changed lines plus <paramref name="contextLines"/> of
    /// surrounding context on each side (collapsing anything further away
    /// behind a "…" marker) — a pretty-printed FormXML/FetchXML document can
    /// still run to a few thousand lines, and a single small edit shouldn't
    /// mean scrolling through all of them to find it.
    /// </summary>
    public static void PrintDiff(IReadOnlyList<TextDiffLine> diff, int contextLines = 2)
    {
        var changedIndices = new List<int>();
        for (var i = 0; i < diff.Count; i++)
        {
            if (diff[i].Kind != TextDiffLineKind.Unchanged)
            {
                changedIndices.Add(i);
            }
        }

        if (changedIndices.Count == 0)
        {
            return;
        }

        var ranges = new List<(int Start, int End)>();
        foreach (var index in changedIndices)
        {
            var start = Math.Max(0, index - contextLines);
            var end = Math.Min(diff.Count - 1, index + contextLines);
            if (ranges.Count > 0 && start <= ranges[^1].End + 1)
            {
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
            }
            else
            {
                ranges.Add((start, end));
            }
        }

        for (var r = 0; r < ranges.Count; r++)
        {
            if (r > 0)
            {
                AnsiConsole.MarkupLine("[grey]  …[/]");
            }

            for (var i = ranges[r].Start; i <= ranges[r].End; i++)
            {
                var line = diff[i];
                var text = line.Text.EscapeMarkup();
                AnsiConsole.MarkupLine(line.Kind switch
                {
                    TextDiffLineKind.Added => $"[green]+ {text}[/]",
                    TextDiffLineKind.Removed => $"[red]- {text}[/]",
                    _ => $"[grey]  {text}[/]",
                });
            }
        }
    }
}

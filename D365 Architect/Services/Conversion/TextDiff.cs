namespace D365Architect.Services.Conversion;

/// <summary>How one line of <see cref="TextDiff.Compute"/>'s result relates to the old/new text.</summary>
public enum TextDiffLineKind
{
    Unchanged,
    Added,
    Removed,
}

/// <summary>One line of a <see cref="TextDiff.Compute"/> result.</summary>
public sealed record TextDiffLine(TextDiffLineKind Kind, string Text);

/// <summary>
/// Generic line-level diff — how <c>form import</c> answers "must have a
/// way to check differences between client and server" (the actual,
/// literal requirement this exists for). Used to diff the two sides'
/// pretty-printed FormXML: <see cref="FormImportPreview.ExistingComparableFormXml"/>
/// against <see cref="FormImportPreview.NewFormXml"/> — deliberately not
/// the live document's own raw FormXML, which was tried first and
/// confirmed live, on a real, richly-customized form, to be nearly
/// useless: every tab/section/cell showed as "changed" purely from
/// resynthesized ids, even when nothing a human actually edited changed at
/// all. See <see cref="FormImportPreview.ExistingComparableFormXml"/>'s own
/// doc comment for how comparing two sides rebuilt through the identical
/// pipeline fixes that — the same reason <c>terraform plan</c> diffs
/// against a normalized view of current state rather than a provider's raw
/// last-applied payload.
/// </summary>
public static class TextDiff
{
    /// <returns>A line-level diff, in document order. Empty when both texts are identical.</returns>
    public static IReadOnlyList<TextDiffLine> Compute(string oldText, string newText)
    {
        var oldLines = oldText.Replace("\r\n", "\n").Split('\n');
        var newLines = newText.Replace("\r\n", "\n").Split('\n');
        return ComputeLcsDiff(oldLines, newLines);
    }

    /// <summary>
    /// Classic dynamic-programming LCS diff (see e.g. Myers' 1986 survey of
    /// diff algorithms for the general technique). O(n·m) time and space —
    /// entirely fine for the few hundred lines a form's decomposed YAML
    /// comes out to, and simple/obviously-correct beats asymptotically
    /// better here for a tool run interactively, not in a hot loop.
    /// </summary>
    private static List<TextDiffLine> ComputeLcsDiff(string[] oldLines, string[] newLines)
    {
        var n = oldLines.Length;
        var m = newLines.Length;
        var lcsLength = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcsLength[i, j] = oldLines[i] == newLines[j]
                    ? lcsLength[i + 1, j + 1] + 1
                    : Math.Max(lcsLength[i + 1, j], lcsLength[i, j + 1]);
            }
        }

        var result = new List<TextDiffLine>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (oldLines[x] == newLines[y])
            {
                result.Add(new TextDiffLine(TextDiffLineKind.Unchanged, oldLines[x]));
                x++;
                y++;
            }
            else if (lcsLength[x + 1, y] >= lcsLength[x, y + 1])
            {
                result.Add(new TextDiffLine(TextDiffLineKind.Removed, oldLines[x]));
                x++;
            }
            else
            {
                result.Add(new TextDiffLine(TextDiffLineKind.Added, newLines[y]));
                y++;
            }
        }

        while (x < n)
        {
            result.Add(new TextDiffLine(TextDiffLineKind.Removed, oldLines[x]));
            x++;
        }

        while (y < m)
        {
            result.Add(new TextDiffLine(TextDiffLineKind.Added, newLines[y]));
            y++;
        }

        return result;
    }
}

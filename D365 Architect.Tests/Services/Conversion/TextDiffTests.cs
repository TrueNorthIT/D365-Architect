using D365Architect.Services.Conversion;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

public sealed class TextDiffTests
{
    [Fact]
    public void Compute_IdenticalText_ProducesAllUnchangedLines()
    {
        var diff = TextDiff.Compute("a\nb\nc", "a\nb\nc");
        Assert.All(diff, line => Assert.Equal(TextDiffLineKind.Unchanged, line.Kind));
        Assert.Equal(3, diff.Count);
    }

    [Fact]
    public void Compute_AppendedLine_ShowsOnlyTheAppendedLineAsAdded()
    {
        var diff = TextDiff.Compute("a\nb", "a\nb\nc");

        Assert.Equal(3, diff.Count);
        Assert.Equal(TextDiffLineKind.Unchanged, diff[0].Kind);
        Assert.Equal(TextDiffLineKind.Unchanged, diff[1].Kind);
        Assert.Equal(TextDiffLineKind.Added, diff[2].Kind);
        Assert.Equal("c", diff[2].Text);
    }

    [Fact]
    public void Compute_RemovedLine_ShowsOnlyTheRemovedLineAsRemoved()
    {
        var diff = TextDiff.Compute("a\nb\nc", "a\nc");

        Assert.Contains(diff, l => l.Kind == TextDiffLineKind.Removed && l.Text == "b");
        Assert.DoesNotContain(diff, l => l.Kind == TextDiffLineKind.Added);
    }

    [Fact]
    public void Compute_ChangedLine_ShowsRemovedThenAdded()
    {
        var diff = TextDiff.Compute("a\nb\nc", "a\nB\nc");

        Assert.Contains(diff, l => l.Kind == TextDiffLineKind.Removed && l.Text == "b");
        Assert.Contains(diff, l => l.Kind == TextDiffLineKind.Added && l.Text == "B");
    }

    [Fact]
    public void Compute_CrlfAndLf_AreTreatedAsEquivalent()
    {
        // Compute normalizes \r\n to \n on both sides before diffing.
        var diff = TextDiff.Compute("a\r\nb", "a\nb");
        Assert.All(diff, line => Assert.Equal(TextDiffLineKind.Unchanged, line.Kind));
    }

    [Fact]
    public void Compute_BothEmpty_ProducesOneUnchangedEmptyLine()
    {
        // "".Split('\n') yields a single empty-string element, not zero
        // elements, so this is one Unchanged line with empty text - not an
        // empty diff.
        var diff = TextDiff.Compute("", "");
        var line = Assert.Single(diff);
        Assert.Equal(TextDiffLineKind.Unchanged, line.Kind);
        Assert.Equal("", line.Text);
    }

    [Fact]
    public void Compute_WhollyDifferentText_MarksOldLinesRemovedAndNewLinesAdded()
    {
        var diff = TextDiff.Compute("x\ny", "a\nb\nc");

        Assert.Equal(["x", "y"], diff.Where(l => l.Kind == TextDiffLineKind.Removed).Select(l => l.Text));
        Assert.Equal(["a", "b", "c"], diff.Where(l => l.Kind == TextDiffLineKind.Added).Select(l => l.Text));
    }
}

using D365Architect.Commands;
using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Spectre.Console;
using Xunit;

namespace D365Architect.Tests;

/// <summary>Covers the preview-all, confirm-once, apply-all flow behind `solution import`, plus the shared `--table` matching.</summary>
[Collection("Console")]
public sealed class SolutionImportFlowTests
{
    private sealed class Item
    {
        public int Applied { get; private set; }
        public SolutionImportItem Value { get; }

        public Item(string path, ImportOutcome outcome, bool applySucceeds = true, bool throwOnPreview = false)
        {
            Value = new SolutionImportItem(path, (_, _) =>
            {
                if (throwOnPreview)
                {
                    throw new InvalidOperationException("boom");
                }

                return Task.FromResult(outcome == ImportOutcome.Changed
                    ? new PreparedImport(outcome, _ => { Applied++; return Task.FromResult(applySucceeds); })
                    : new PreparedImport(outcome));
            });
        }
    }

    private static Task<int> Run(IEnumerable<Item> items, bool yes, bool whatIf, Func<string, bool>? confirm) =>
        SolutionImportFlow.RunAsync(items.Select(i => i.Value).ToList(), yes, whatIf, verbose: false, confirm, CancellationToken.None);

    [Fact]
    public async Task ManyFilesFewChanged_AsksOnceAndAppliesOnlyChanged()
    {
        var items = new[]
        {
            new Item("a", ImportOutcome.Unchanged), new Item("b", ImportOutcome.Changed), new Item("c", ImportOutcome.Unchanged),
            new Item("d", ImportOutcome.Changed), new Item("e", ImportOutcome.Changed),
        };
        var prompts = new List<string>();

        var exit = await Run(items, yes: false, whatIf: false, confirm: p => { prompts.Add(p); return true; });

        Assert.Equal(0, exit);
        Assert.Equal(["Import 3 changes?"], prompts);
        Assert.Equal([0, 1, 0, 1, 1], items.Select(i => i.Applied));
    }

    [Fact]
    public async Task Declined_AppliesNothing()
    {
        var items = new[] { new Item("a", ImportOutcome.Changed) };

        var exit = await Run(items, yes: false, whatIf: false, confirm: _ => false);

        Assert.Equal(0, exit);
        Assert.Equal(0, items[0].Applied);
    }

    [Fact]
    public async Task Yes_SkipsThePrompt()
    {
        var items = new[] { new Item("a", ImportOutcome.Changed) };

        var exit = await Run(items, yes: true, whatIf: false, confirm: _ => throw new InvalidOperationException("prompted"));

        Assert.Equal(0, exit);
        Assert.Equal(1, items[0].Applied);
    }

    [Fact]
    public async Task WhatIf_NeverPromptsOrApplies()
    {
        var items = new[] { new Item("a", ImportOutcome.Changed) };

        var exit = await Run(items, yes: true, whatIf: true, confirm: _ => throw new InvalidOperationException("prompted"));

        Assert.Equal(0, exit);
        Assert.Equal(0, items[0].Applied);
    }

    [Fact]
    public async Task FailedPreview_DoesNotStopOthers_IsNotApplied_AndExitsNonZero()
    {
        var items = new[]
        {
            new Item("bad", ImportOutcome.Failed), new Item("throws", ImportOutcome.Changed, throwOnPreview: true), new Item("good", ImportOutcome.Changed),
        };
        var prompts = new List<string>();

        var exit = await Run(items, yes: false, whatIf: false, confirm: p => { prompts.Add(p); return true; });

        Assert.Equal(1, exit);
        Assert.Equal(["Import 1 change? (2 failed and will be skipped)"], prompts);
        Assert.Equal([0, 0, 1], items.Select(i => i.Applied));
    }

    [Fact]
    public async Task ApplyFailure_ExitsNonZeroButOthersStillApplied()
    {
        var items = new[] { new Item("a", ImportOutcome.Changed, applySucceeds: false), new Item("b", ImportOutcome.Changed) };

        var exit = await Run(items, yes: true, whatIf: false, confirm: null);

        Assert.Equal(1, exit);
        Assert.Equal([1, 1], items.Select(i => i.Applied));
    }

    [Fact]
    public async Task NothingChanged_NoPromptAndExitsZero()
    {
        var items = new[] { new Item("a", ImportOutcome.Unchanged) };

        var exit = await Run(items, yes: false, whatIf: false, confirm: _ => throw new InvalidOperationException("prompted"));

        Assert.Equal(0, exit);
    }

    private static Task<(int Exit, string Output)> RunCaptured(IEnumerable<Item> items, bool yes, bool whatIf, Func<string, bool>? confirm, bool verbose = false) =>
        ConsoleCapture.RunAsync(() => SolutionImportFlow.RunAsync(items.Select(i => i.Value).ToList(), yes, whatIf, verbose, confirm, CancellationToken.None));

    // Nothing is written after --whatif or a declined prompt, so
    // the summary must not claim anything "changed".
    [Fact]
    public async Task WhatIf_SummaryReadsWouldChangeNotChanged()
    {
        var (_, output) = await RunCaptured([new Item("a", ImportOutcome.Changed)], yes: false, whatIf: true, confirm: null);

        Assert.Contains("1 would change, 0 unchanged, 0 failed", output);
        Assert.DoesNotContain("1 changed", output);
    }

    [Fact]
    public async Task Declined_SummaryReadsWouldChangeNotChanged()
    {
        var (_, output) = await RunCaptured([new Item("a", ImportOutcome.Changed)], yes: false, whatIf: false, confirm: _ => false);

        Assert.Contains("Aborted.", output);
        Assert.Contains("1 would change, 0 unchanged, 0 failed", output);
        Assert.DoesNotContain("1 changed", output);
    }

    [Fact]
    public async Task Applied_SummaryReadsChanged()
    {
        var (_, output) = await RunCaptured([new Item("a", ImportOutcome.Changed)], yes: true, whatIf: false, confirm: null);

        Assert.Contains("1 changed, 0 unchanged, 0 failed", output);
    }

    // An unchanged asset that nevertheless printed something (e.g. a warning or a
    // --verbose line) is still separated from the next asset by a blank line;
    // a silent one adds none.
    [Fact]
    public async Task UnchangedAssetThatPrinted_IsFollowedByABlankLine_ASilentOneIsNot()
    {
        var chatty = new SolutionImportItem("chatty", (_, _) =>
        {
            AnsiConsole.MarkupLine("CHATTY-OUTPUT");
            return Task.FromResult(PreparedImport.Unchanged with { PrintedOutput = true });
        });
        var silent = new SolutionImportItem("silent", (_, _) => Task.FromResult(PreparedImport.Unchanged));

        var (_, output) = await ConsoleCapture.RunAsync(() => SolutionImportFlow.RunAsync([chatty, silent], yes: false, whatIf: false, verbose: false, confirm: null, CancellationToken.None));

        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var chattyAt = lines.IndexOf("CHATTY-OUTPUT");
        Assert.Equal("", lines[chattyAt + 1]);
        Assert.StartsWith("0 changed", lines[chattyAt + 2]);
    }
}

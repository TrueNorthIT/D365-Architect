using Spectre.Console;

namespace D365Architect.Commands.Solution;

/// <summary>One file `solution import` will preview: its path (for error messages) and how to preview it.</summary>
/// <param name="Path">The YAML file's path.</param>
/// <param name="PreviewAsync">Previews the file without prompting or writing; the bool is whether to print "No changes" if it's unchanged.</param>
public sealed record SolutionImportItem(string Path, Func<bool, CancellationToken, Task<PreparedImport>> PreviewAsync);

/// <summary>
/// The preview-all → confirm-once → apply-all flow behind `solution import`.
/// Every item is previewed first (<see cref="PreviewAsync"/>: diffs printed as
/// they go, unchanged ones silent unless <c>verbose</c>); then
/// <see cref="ConfirmAndApplyAsync"/> asks one question covering every
/// changed asset and applies them. <see cref="RunAsync"/> does both for a
/// list known up front; <c>solution import</c> previews one step at a time
/// as <see cref="Services.Conversion.SolutionImportOrchestrator"/> hands them
/// over, then confirms once at the end.
///
/// Failures: an item whose preview fails (or is refused by its own safety
/// gate, e.g. a form schema violation) is reported and never applied, but
/// doesn't stop the others being previewed. After the prompt the remaining
/// changed assets are still applied (same "one bad file doesn't block the
/// rest" behaviour as before); the prompt says how many failed and will be
/// skipped, and the exit code is non-zero if anything failed. There's no
/// cross-asset rollback.
/// </summary>
public sealed class SolutionImportFlow(bool verbose)
{
    private readonly List<(SolutionImportItem Item, PreparedImport Prepared)> _changed = [];
    private int _unchanged;
    private int _failed;
    private int _skipped;

    public static async Task<int> RunAsync(IReadOnlyList<SolutionImportItem> items, bool yes, bool whatIf, bool verbose, Func<string, bool>? confirm, CancellationToken cancellationToken)
    {
        var flow = new SolutionImportFlow(verbose);
        foreach (var item in items)
        {
            await flow.PreviewAsync(item, cancellationToken);
        }

        return await flow.ConfirmAndApplyAsync(yes, whatIf, confirm, cancellationToken);
    }

    /// <summary>Previews one item and holds on to it if it changed. Never prompts or writes; an exception is reported as a failure.</summary>
    public async Task<ImportOutcome> PreviewAsync(SolutionImportItem item, CancellationToken cancellationToken)
    {
        PreparedImport prepared;
        try
        {
            prepared = await item.PreviewAsync(verbose, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ErrorConsole.Print($"'{item.Path}' failed: {ex.Message}");
            prepared = PreparedImport.Failed;
        }

        switch (prepared.Outcome)
        {
            case ImportOutcome.Changed:
                _changed.Add((item, prepared));
                break;
            case ImportOutcome.Unchanged:
                _unchanged++;
                break;
            default:
                _failed++;
                break;
        }

        if (prepared.Outcome != ImportOutcome.Unchanged || prepared.PrintedOutput)
        {
            AnsiConsole.WriteLine();
        }

        return prepared.Outcome;
    }

    /// <summary>Counts an asset that was never previewed (its table would be created first under <c>--whatif</c>), for the summary.</summary>
    public void CountSkipped() => _skipped++;

    /// <summary>Asks once (unless <c>yes</c>; never under <c>whatIf</c>) and applies every changed asset previewed so far. Returns the exit code.</summary>
    public async Task<int> ConfirmAndApplyAsync(bool yes, bool whatIf, Func<string, bool>? confirm, CancellationToken cancellationToken)
    {
        confirm ??= message => AnsiConsole.Confirm(message, defaultValue: false);

        if (_changed.Count == 0)
        {
            PrintSummary(0, written: true);
            return ExitCode;
        }

        if (whatIf)
        {
            AnsiConsole.MarkupLine("[grey]--whatif: nothing was written.[/]");
            PrintSummary(_changed.Count, written: false);
            return ExitCode;
        }

        if (!yes)
        {
            var prompt = $"Import {_changed.Count} change{(_changed.Count == 1 ? "" : "s")}?"
                + (_failed > 0 ? $" ({_failed} failed and will be skipped)" : "");
            if (!confirm(prompt))
            {
                AnsiConsole.MarkupLine("[yellow]Aborted.[/] Nothing was written.");
                PrintSummary(_changed.Count, written: false);
                return ExitCode;
            }
        }

        var applied = 0;
        foreach (var (item, prepared) in _changed)
        {
            bool ok;
            try
            {
                ok = await prepared.ApplyAsync!(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorConsole.Print($"'{item.Path}' failed: {ex.Message}");
                ok = false;
            }

            if (ok)
            {
                applied++;
            }
            else
            {
                _failed++;
            }

            AnsiConsole.WriteLine();
        }

        PrintSummary(applied, written: true);
        return ExitCode;
    }

    private int ExitCode => _failed == 0 ? 0 : 1;

    // Written is false after --whatif or an aborted prompt, where nothing was applied: the count is what would change.
    private void PrintSummary(int changed, bool written)
    {
        var colour = _failed == 0 ? "green" : "yellow";
        var changedText = written ? $"{changed} changed" : $"{changed} would change";
        var skippedText = _skipped > 0 ? $", {_skipped} skipped" : "";
        AnsiConsole.MarkupLine($"[{colour}]{changedText}, {_unchanged} unchanged, {_failed} failed{skippedText}[/]");
    }
}

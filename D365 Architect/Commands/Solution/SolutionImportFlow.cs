using Spectre.Console;

namespace D365Architect.Commands.Solution;

/// <summary>One file `solution import` will preview: its path (for error messages) and how to preview it.</summary>
/// <param name="Path">The YAML file's path.</param>
/// <param name="PreviewAsync">Previews the file without prompting or writing; the bool is whether to print "No changes" if it's unchanged.</param>
public sealed record SolutionImportItem(string Path, Func<bool, CancellationToken, Task<PreparedImport>> PreviewAsync);

/// <summary>
/// The preview-all → confirm-once → apply-all flow behind `solution import`.
/// Every item is previewed first (diffs printed as they go, unchanged ones
/// silent unless <c>verbose</c>); then one prompt covers every changed asset.
///
/// Failures: an item whose preview fails (or is refused by its own safety
/// gate, e.g. a form schema violation) is reported and never applied, but
/// doesn't stop the others being previewed. After the prompt the remaining
/// changed assets are still applied (same "one bad file doesn't block the
/// rest" behaviour as before); the prompt says how many failed and will be
/// skipped, and the exit code is non-zero if anything failed. There's no
/// cross-asset rollback.
/// </summary>
public static class SolutionImportFlow
{
    public static async Task<int> RunAsync(IReadOnlyList<SolutionImportItem> items, bool yes, bool whatIf, bool verbose, Func<string, bool>? confirm, CancellationToken cancellationToken)
    {
        confirm ??= message => AnsiConsole.Confirm(message, defaultValue: false);

        var unchanged = 0;
        var failed = 0;
        var changed = new List<(SolutionImportItem Item, PreparedImport Prepared)>();

        foreach (var item in items)
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
                    changed.Add((item, prepared));
                    break;
                case ImportOutcome.Unchanged:
                    unchanged++;
                    break;
                default:
                    failed++;
                    break;
            }

            if (prepared.Outcome != ImportOutcome.Unchanged || prepared.PrintedOutput)
            {
                AnsiConsole.WriteLine();
            }
        }

        if (changed.Count == 0)
        {
            PrintSummary(0, unchanged, failed, written: true);
            return failed == 0 ? 0 : 1;
        }

        if (whatIf)
        {
            AnsiConsole.MarkupLine("[grey]--whatif: nothing was written.[/]");
            PrintSummary(changed.Count, unchanged, failed, written: false);
            return failed == 0 ? 0 : 1;
        }

        if (!yes)
        {
            var prompt = $"Import {changed.Count} change{(changed.Count == 1 ? "" : "s")}?"
                + (failed > 0 ? $" ({failed} failed and will be skipped)" : "");
            if (!confirm(prompt))
            {
                AnsiConsole.MarkupLine("[yellow]Aborted.[/] Nothing was written.");
                PrintSummary(changed.Count, unchanged, failed, written: false);
                return failed == 0 ? 0 : 1;
            }
        }

        var applied = 0;
        foreach (var (item, prepared) in changed)
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
                failed++;
            }

            AnsiConsole.WriteLine();
        }

        PrintSummary(applied, unchanged, failed, written: true);
        return failed == 0 ? 0 : 1;
    }

    // Written is false after --whatif or an aborted prompt, where nothing was applied: the count is what would change.
    private static void PrintSummary(int changed, int unchanged, int failed, bool written)
    {
        var colour = failed == 0 ? "green" : "yellow";
        var changedText = written ? $"{changed} changed" : $"{changed} would change";
        AnsiConsole.MarkupLine($"[{colour}]{changedText}, {unchanged} unchanged, {failed} failed[/]");
    }
}

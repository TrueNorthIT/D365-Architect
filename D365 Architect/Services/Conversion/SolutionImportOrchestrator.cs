namespace D365Architect.Services.Conversion;

/// <summary>The kinds of step <c>solution import</c> runs, one per asset file.</summary>
public enum SolutionImportStage
{
    /// <summary>Create the table in a <c>*.table.yml</c> if it isn't live yet (<c>table create</c>'s flow); a table that already exists is a success.</summary>
    CreateTable,

    /// <summary><c>choices.yml</c> at the folder's root (<c>choice import</c>'s flow).</summary>
    ImportChoices,

    /// <summary>A <c>*.table.yml</c>'s columns (<c>table import</c>'s flow).</summary>
    ImportTable,

    /// <summary>A <c>*.view.yml</c> (<c>view import</c>'s flow).</summary>
    ImportView,

    /// <summary>A <c>*.form.yml</c> (<c>form import</c>'s flow).</summary>
    ImportForm,
}

/// <summary>How one step ended, as far as the rest of the run is concerned.</summary>
public enum SolutionImportStepOutcome
{
    /// <summary>Done: imported, created, already existed, or (under <c>--whatif</c>) previewed.</summary>
    Succeeded,

    /// <summary>The step has already reported its own error.</summary>
    Failed,

    /// <summary><see cref="SolutionImportStage.CreateTable"/> under <c>--whatif</c>: the table doesn't exist and would be created, so nothing else of its folder can be previewed.</summary>
    WouldCreate,

    /// <summary><see cref="SolutionImportStage.CreateTable"/>: the user said no at the confirmation prompt, so the table still doesn't exist.</summary>
    Declined,
}

/// <summary>
/// Does one file's worth of work for one stage - in production, the
/// matching per-asset command's <c>RunAsync</c> (so each step keeps its own
/// preview, diff, confirm and apply). An exception is treated as
/// <see cref="SolutionImportStepOutcome.Failed"/>, as <c>solution import</c>
/// always has.
/// </summary>
public interface ISolutionImportStepRunner
{
    Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken);

    /// <summary>Reports a step not run because its table (<paramref name="tableLogicalName"/>, the folder's name) would be created first under <c>--whatif</c>, so there is nothing live to preview it against.</summary>
    void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName);
}

/// <param name="Succeeded">Steps that succeeded (a <see cref="SolutionImportStepOutcome.WouldCreate"/> included).</param>
/// <param name="Failed">Steps that failed (a declined create included).</param>
/// <param name="StoppedAtAssetPath">Set when a <see cref="SolutionImportStage.CreateTable"/> step failed or was declined and the later stages were not attempted: the <c>*.table.yml</c> path whose table wasn't created. Null otherwise.</param>
/// <param name="StopWasDeclined">True when the stop at <paramref name="StoppedAtAssetPath"/> was the user declining the create, not a failure.</param>
/// <param name="Skipped">Steps skipped because their table would be created first (<c>--whatif</c> only).</param>
public sealed record SolutionImportResult(int Succeeded, int Failed, string? StoppedAtAssetPath, bool StopWasDeclined = false, int Skipped = 0);

/// <summary>
/// The stage ordering <c>solution import</c> follows, lifted out of
/// <c>ImportSolutionCommand</c> so it can be tested without Spectre or a
/// console: create every missing table first, then (each stage reading live
/// state afresh, because no preview is built before creation finishes) the
/// same sequence as before - choices, then per table folder its table, views
/// and forms. Never writes to the input folder. Behaviour pinned by
/// <c>SolutionImportOrchestratorTests</c>.
///
/// Failure handling differs by stage on purpose. A failed view or form (or
/// choice/column import) doesn't stop the rest, as always. A failed create
/// stops the whole run at that table - no further creates and no later
/// stages: every later stage of that table would fail against a table that
/// isn't there, and a later table's lookups may target it, so carrying on
/// only buries the one error that matters under a cascade of 404s. A
/// declined create stops the run the same way, for the same reason.
///
/// Under <c>--whatif</c> nothing is created, so a table that would be is
/// still missing when the later stages preview: every later step in that
/// table's folder (its own table import, views and forms - the layout
/// <c>solution export</c> writes) is skipped and reported as such rather
/// than counted as a failure.
/// </summary>
public sealed class SolutionImportOrchestrator
{
    public async Task<SolutionImportResult> RunAsync(string inputDirectory, ISolutionImportStepRunner runner, CancellationToken cancellationToken)
    {
        var tableDirectories = Sorted(Directory.GetDirectories(inputDirectory));
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var wouldCreateDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tablePath in tableDirectories.SelectMany(d => Sorted(Directory.GetFiles(d, "*.table.yml"))))
        {
            var outcome = await TryRunAsync(runner, SolutionImportStage.CreateTable, tablePath, cancellationToken);
            if (outcome is SolutionImportStepOutcome.Failed or SolutionImportStepOutcome.Declined)
            {
                return new SolutionImportResult(succeeded, failed + 1, StoppedAtAssetPath: tablePath, StopWasDeclined: outcome == SolutionImportStepOutcome.Declined);
            }

            if (outcome == SolutionImportStepOutcome.WouldCreate)
            {
                wouldCreateDirectories.Add(Path.GetDirectoryName(tablePath)!);
            }

            succeeded++;
        }

        var steps = new List<(SolutionImportStage Stage, string Path)>();
        var choicesPath = Path.Combine(inputDirectory, "choices.yml");
        if (File.Exists(choicesPath))
        {
            steps.Add((SolutionImportStage.ImportChoices, choicesPath));
        }

        foreach (var directory in tableDirectories)
        {
            steps.AddRange(Sorted(Directory.GetFiles(directory, "*.table.yml")).Select(p => (SolutionImportStage.ImportTable, p)));
            steps.AddRange(Sorted(Directory.GetFiles(directory, "*.view.yml")).Select(p => (SolutionImportStage.ImportView, p)));
            steps.AddRange(Sorted(Directory.GetFiles(directory, "*.form.yml")).Select(p => (SolutionImportStage.ImportForm, p)));
        }

        foreach (var (stage, path) in steps)
        {
            var directory = Path.GetDirectoryName(path)!;
            if (wouldCreateDirectories.Contains(directory))
            {
                runner.ReportSkipped(stage, path, Path.GetFileName(directory));
                skipped++;
            }
            else if (await TryRunAsync(runner, stage, path, cancellationToken) == SolutionImportStepOutcome.Failed)
            {
                failed++;
            }
            else
            {
                succeeded++;
            }
        }

        return new SolutionImportResult(succeeded, failed, StoppedAtAssetPath: null, Skipped: skipped);
    }

    private static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// A step that throws is a failure, not a crash. The production runner
    /// already catches and prints its own exceptions (this layer never
    /// writes to the console); this is the backstop that keeps one throwing
    /// step from aborting the run regardless.
    /// </summary>
    private static async Task<SolutionImportStepOutcome> TryRunAsync(ISolutionImportStepRunner runner, SolutionImportStage stage, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await runner.RunAsync(stage, path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SolutionImportStepOutcome.Failed;
        }
    }
}

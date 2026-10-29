namespace D365Architect.Services.Conversion;

/// <summary>The kinds of step <c>solution import</c> runs, one per asset file.</summary>
public enum SolutionImportStage
{
    /// <summary>
    /// No longer run: tables are created by <see cref="ISolutionImportStepRunner.CreateTablesAsync"/>, once for the whole run.
    /// Kept only because test runners still tell it apart from the import stages.
    /// </summary>
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

    /// <summary>The create stage only: the user said no at the confirmation prompt, so no table was created.</summary>
    Declined,
}

/// <summary>
/// What <see cref="ISolutionImportStepRunner.CreateTablesAsync"/> reports: the whole create stage, which is one decision and one transaction.
/// </summary>
/// <param name="Outcome"><see cref="SolutionImportStepOutcome.Succeeded"/> (every missing table was created, or none was missing, or - under <c>--whatif</c> - nothing was written),
/// <see cref="SolutionImportStepOutcome.Failed"/> (the transaction was rolled back, or a table file was refused: none was created) or
/// <see cref="SolutionImportStepOutcome.Declined"/> (the user said no: none was created).</param>
/// <param name="WouldCreateAssetPaths">Under <c>--whatif</c>: the <c>*.table.yml</c> paths of the tables that don't exist and would be created; their other files are skipped. Empty otherwise.</param>
/// <param name="FailedAssetPath">For <see cref="SolutionImportStepOutcome.Failed"/>: the <c>*.table.yml</c> path of the table that failed, when it can be told.</param>
public sealed record SolutionTableCreateResult(SolutionImportStepOutcome Outcome, IReadOnlyList<string> WouldCreateAssetPaths, string? FailedAssetPath = null);

/// <summary>
/// Does one file's worth of work for one stage - in production, the
/// matching per-asset command's <c>PreviewAsync</c> (so each step keeps its
/// own preview, diff and safety gates), holding any change until
/// <c>solution import</c> asks once and applies them all after the last
/// step. An exception is treated as
/// <see cref="SolutionImportStepOutcome.Failed"/>, as <c>solution import</c>
/// always has.
/// </summary>
public interface ISolutionImportStepRunner
{
    Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken);

    /// <summary>
    /// The create stage, once for the whole run: given every <c>*.table.yml</c> path (sorted, so the order is stable), lists the tables
    /// that don't exist, asks ONCE ("Create these N tables?", unless <c>--yes</c>; never under <c>--whatif</c>) and creates them all in one
    /// transaction (<see cref="ITableCreateService.ApplyAllAsync"/>).
    /// </summary>
    Task<SolutionTableCreateResult> CreateTablesAsync(IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken);

    /// <summary>Reports a step not run because its table (<paramref name="tableLogicalName"/>, the folder's name) would be created first under <c>--whatif</c>, so there is nothing live to preview it against.</summary>
    void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName);
}

/// <param name="Succeeded">Import steps that succeeded (the create stage isn't counted).</param>
/// <param name="Failed">Steps that failed (a failed or declined create stage included).</param>
/// <param name="StoppedAtAssetPath">Set when the create stage failed or was declined and the later stages were not attempted: the <c>*.table.yml</c> path whose table failed,
/// or the input folder when that can't be told (always, for a decline). Null otherwise.</param>
/// <param name="StopWasDeclined">True when the stop at <paramref name="StoppedAtAssetPath"/> was the user declining the create, not a failure.</param>
/// <param name="Skipped">Steps skipped because their table would be created first (<c>--whatif</c> only).</param>
public sealed record SolutionImportResult(int Succeeded, int Failed, string? StoppedAtAssetPath, bool StopWasDeclined = false, int Skipped = 0);

/// <summary>
/// The stage ordering <c>solution import</c> follows, lifted out of
/// <c>ImportSolutionCommand</c> so it can be tested without Spectre or a
/// console: create every missing table first (one call to
/// <see cref="ISolutionImportStepRunner.CreateTablesAsync"/>: one question,
/// one transaction), then (each stage reading live
/// state afresh, because no preview is built before creation finishes) the
/// same sequence as before - choices, then per table folder its table, views
/// and forms. Never writes to the input folder. Behaviour pinned by
/// <c>SolutionImportOrchestratorTests</c>.
///
/// Failure handling differs by stage on purpose. A failed view or form (or
/// choice/column import) doesn't stop the rest, as always. A failed create
/// stage (nothing was created: the transaction rolled back) stops the whole
/// run - no later stages: every later stage of a missing table would fail
/// against a table that isn't there, and other tables' lookups may target
/// it, so carrying on only buries the one error that matters under a
/// cascade of 404s. A declined create stops the run the same way, for the
/// same reason.
///
/// Under <c>--whatif</c> nothing is created, so a table that would be is
/// still missing when the later stages preview: every later step in that
/// table's folder (its own table import, views and forms - the layout
/// <c>solution export</c> writes) is skipped and reported as such rather
/// than counted as a failure.
/// </summary>
public sealed class SolutionImportOrchestrator
{
    public Task<SolutionImportResult> RunAsync(string inputDirectory, ISolutionImportStepRunner runner, CancellationToken cancellationToken) =>
        RunAsync(inputDirectory, runner, onlyTables: null, cancellationToken);

    /// <summary>
    /// As above, limited by <c>--table</c>: when <paramref name="onlyTables"/>
    /// is set, only those table folders (names matched case-insensitively,
    /// already checked against the folder by <see cref="SolutionTableFilter"/>)
    /// are created and imported, and <c>choices.yml</c> is skipped. Null runs
    /// everything.
    /// </summary>
    public async Task<SolutionImportResult> RunAsync(string inputDirectory, ISolutionImportStepRunner runner, IReadOnlyCollection<string>? onlyTables, CancellationToken cancellationToken)
    {
        var tableDirectories = Sorted(Directory.GetDirectories(inputDirectory)
            .Where(d => onlyTables is null || onlyTables.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)));
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;

        // The create stage: every *.table.yml at once - one question, one transaction.
        var tablePaths = tableDirectories.SelectMany(d => Sorted(Directory.GetFiles(d, "*.table.yml"))).ToList();
        var create = tablePaths.Count == 0
            ? new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, [])
            : await TryCreateTablesAsync(runner, tablePaths, cancellationToken);
        if (create.Outcome is SolutionImportStepOutcome.Failed or SolutionImportStepOutcome.Declined)
        {
            return new SolutionImportResult(0, 1, StoppedAtAssetPath: create.FailedAssetPath ?? inputDirectory, StopWasDeclined: create.Outcome == SolutionImportStepOutcome.Declined);
        }

        var wouldCreateDirectories = create.WouldCreateAssetPaths.Select(p => Path.GetDirectoryName(p)!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var steps = new List<(SolutionImportStage Stage, string Path)>();
        var choicesPath = Path.Combine(inputDirectory, "choices.yml");
        if (onlyTables is null && File.Exists(choicesPath))
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

    /// <summary>The same backstop for the create stage: a throw is a failed stage (nothing known to be created), which stops the run.</summary>
    private static async Task<SolutionTableCreateResult> TryCreateTablesAsync(ISolutionImportStepRunner runner, IReadOnlyList<string> tablePaths, CancellationToken cancellationToken)
    {
        try
        {
            return await runner.CreateTablesAsync(tablePaths, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SolutionTableCreateResult(SolutionImportStepOutcome.Failed, []);
        }
    }
}

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

/// <summary>
/// Does one file's worth of work for one stage - in production, the
/// matching per-asset command's <c>RunAsync</c> (so each step keeps its own
/// preview, diff, confirm and apply). Returns true on success; false means
/// the step has already reported its own error. An exception is treated as a
/// failure, as <c>solution import</c> always has.
/// </summary>
public interface ISolutionImportStepRunner
{
    Task<bool> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken);
}

/// <param name="Succeeded">Steps that succeeded.</param>
/// <param name="Failed">Steps that failed.</param>
/// <param name="StoppedAtAssetPath">Set when a <see cref="SolutionImportStage.CreateTable"/> step failed and the later stages were not attempted: the <c>*.table.yml</c> path whose table could not be created. Null otherwise.</param>
public sealed record SolutionImportResult(int Succeeded, int Failed, string? StoppedAtAssetPath);

/// <summary>
/// The stage ordering <c>solution import</c> follows, lifted out of
/// <c>ImportSolutionCommand</c> so it can be tested without Spectre or a
/// console: create every missing table first, then (each stage reading live
/// state afresh, because no preview is built before creation finishes) the
/// same sequence as before - choices, then per table folder its table, views
/// and forms. Never writes to the input folder. Stub added by the
/// test-first pass; behaviour pinned by <c>SolutionImportOrchestratorTests</c>.
/// </summary>
public sealed class SolutionImportOrchestrator
{
    public Task<SolutionImportResult> RunAsync(string inputDirectory, ISolutionImportStepRunner runner, CancellationToken cancellationToken) => throw new NotImplementedException();
}

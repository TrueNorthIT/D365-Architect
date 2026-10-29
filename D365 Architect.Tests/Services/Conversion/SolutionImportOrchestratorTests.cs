using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="SolutionImportOrchestrator"/> - the stage ordering of
/// <c>solution import</c> (REQ-10), that it never rewrites the folder it
/// reads (REQ-11) and that a failed or declined table create stops the later
/// stages (REQ-12).
///
/// Batch table creation (REQ-7 to REQ-9 of the "publish by default / create tables
/// in one transaction" requirements - tagged with the "Feature" trait
/// "batch-table-create" because those IDs overlap the older ones): the orchestrator
/// hands EVERY <c>*.table.yml</c> to <see cref="ISolutionImportStepRunner.CreateTablesAsync"/>
/// in one call instead of running one create step per table.
///
/// Why a seam: the ordering used to live inline in
/// <c>ImportSolutionCommand.ExecuteAsync</c>, which builds the per-asset
/// commands itself and prints through the static <c>AnsiConsole</c>, so there
/// was no place to observe "all creates first, then everything else". The
/// orchestrator owns the sequence and hands each step to an
/// <see cref="ISolutionImportStepRunner"/> (in production, the existing
/// per-asset commands' <c>RunAsync</c>). Each step's own behaviour has its own
/// tests; here the thing under test is the sequence, so the runner just
/// records what it was asked to do and which steps it should fail.
/// </summary>
public sealed class SolutionImportOrchestratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public SolutionImportOrchestratorTests()
    {
        // The shape `solution export` writes: choices.yml at the root, one folder per table.
        Write("choices.yml", "- name: tn_priority\n");
        Write("tn_project/tn_project.table.yml", TableCreateFixtures.TableYaml());
        Write("tn_project/Active projects.view.yml", "name: Active projects\n");
        Write("tn_project/Main.form.yml", "name: Main\n");
        Write("tn_task/tn_task.table.yml", "entity: tn_task\n");
        Write("tn_task/All tasks.view.yml", "name: All tasks\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class RecordingRunner : ISolutionImportStepRunner
    {
        public List<(SolutionImportStage Stage, string FileName)> Steps { get; } = [];

        /// <summary>Every <see cref="CreateTablesAsync"/> call: the table file names it was given, in order.</summary>
        public List<IReadOnlyList<string>> CreateBatches { get; } = [];

        /// <summary>Everything the orchestrator asked of the runner, in order: "CreateTables:a,b" for the batch, "Stage:File" for the rest.</summary>
        public List<string> Log { get; } = [];

        /// <summary>What the batch create reports; by default it succeeds and nothing would be created.</summary>
        public Func<IReadOnlyList<string>, SolutionTableCreateResult> CreateResult { get; set; } = _ => new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, []);

        public Task<SolutionTableCreateResult> CreateTablesAsync(IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken)
        {
            var names = tableAssetPaths.Select(Path.GetFileName).Select(n => n!).ToList();
            CreateBatches.Add(names);
            Log.Add($"CreateTables:{string.Join(",", names)}");
            return Task.FromResult(CreateResult(tableAssetPaths));
        }

        /// <summary>Which steps fail; everything else succeeds.</summary>
        public Func<SolutionImportStage, string, bool> Fails { get; set; } = (_, _) => false;

        /// <summary>The outcome of a step that doesn't fail - e.g. a create that would happen under --whatif, or was declined.</summary>
        public Func<SolutionImportStage, string, SolutionImportStepOutcome> Outcome { get; set; } = (_, _) => SolutionImportStepOutcome.Succeeded;

        /// <summary>Steps reported as skipped, as "Stage:FileName (table)".</summary>
        public List<string> Skipped { get; } = [];

        public Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(assetPath);
            Steps.Add((stage, fileName));
            Log.Add($"{stage}:{fileName}");
            return Task.FromResult(Fails(stage, fileName) ? SolutionImportStepOutcome.Failed : Outcome(stage, fileName));
        }

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName) =>
            Skipped.Add($"{stage}:{Path.GetFileName(assetPath)} ({tableLogicalName})");
    }

    private Task<SolutionImportResult> Run(RecordingRunner runner) => new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

    private static string Describe((SolutionImportStage Stage, string FileName) step) => $"{step.Stage}:{step.FileName}";

    // REQ-7: "`solution import` lists every table it would create and asks once" - so every table file goes to the runner in ONE call, not one step each.
    // Code path: SolutionImportOrchestrator.RunAsync -> ISolutionImportStepRunner.CreateTablesAsync(all *.table.yml paths, sorted).
    [Fact]
    [Trait("Requirement", "REQ-7")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_HandsEveryTableFileToTheRunnerInOneBatch()
    {
        var runner = new RecordingRunner();

        await Run(runner);

        Expect.Equal(1, runner.CreateBatches.Count, $"REQ-7: the create stage should be one CreateTablesAsync call for the whole run. Log: <{string.Join(" | ", runner.Log)}>.");
        Expect.Sequence(["tn_project.table.yml", "tn_task.table.yml"], runner.CreateBatches.FirstOrDefault() ?? [], "REQ-7: the batch should hold every *.table.yml, in folder order.");
    }

    // REQ-7: the per-table CreateTable step (a prompt and a request per table) is no longer how tables are created.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_DoesNotRunAPerTableCreateStep()
    {
        var runner = new RecordingRunner();

        await Run(runner);

        var perTable = runner.Log.Where(l => l.StartsWith("CreateTable:", StringComparison.Ordinal)).ToList();
        Expect.Empty(perTable, "REQ-7: tables should be created by the single CreateTablesAsync call, not one CreateTable step per table.");
    }

    // REQ-10 (older numbering, kept): "first create any missing tables, then re-read live state, then import choices, columns, views and forms as today."
    // The batch comes before any other kind of step, then the order is the same as before the change.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_CreatesTheTablesBeforeAnyOtherStageThenFollowsTodaysOrder()
    {
        var runner = new RecordingRunner();

        await Run(runner);

        Expect.Sequence(
            [
                "CreateTables:tn_project.table.yml,tn_task.table.yml",
                "ImportChoices:choices.yml",
                "ImportTable:tn_project.table.yml",
                "ImportView:Active projects.view.yml",
                "ImportForm:Main.form.yml",
                "ImportTable:tn_task.table.yml",
                "ImportView:All tasks.view.yml",
            ],
            runner.Log,
            "REQ-10: solution import should create the tables first, then import choices, then each table folder's columns, views and forms.");
    }

    // A run where the batch succeeds reports no failures and no stop.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_BatchSucceeds_ReportsNoFailuresAndNoStop()
    {
        var runner = new RecordingRunner();

        var result = await Run(runner);

        Expect.Equal(0, result.Failed, "REQ-10: a run where every step succeeds should report no failures.");
        Expect.Equal(null, result.StoppedAtAssetPath, "REQ-10: a run that completed every stage should not report being stopped.");
    }

    // REQ-8: "If any fails, none are created, the run stops before columns/views/forms, and the error names the table that failed."
    // Example: the batch reports tn_task failed -> no choices, columns, views or forms are attempted.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_BatchFails_NoLaterStageIsAttempted()
    {
        var runner = new RecordingRunner { CreateResult = paths => new SolutionTableCreateResult(SolutionImportStepOutcome.Failed, [], FailedAssetPath: paths[1]) };

        await Run(runner);

        Expect.Sequence(["CreateTables:tn_project.table.yml,tn_task.table.yml"], runner.Log, "REQ-8: after the batch failed, no choices, columns, views or forms should be attempted.");
    }

    [Fact]
    [Trait("Requirement", "REQ-8")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_BatchFails_ResultNamesTheTableThatStoppedTheRun()
    {
        var runner = new RecordingRunner { CreateResult = paths => new SolutionTableCreateResult(SolutionImportStepOutcome.Failed, [], FailedAssetPath: paths[1]) };

        var result = await Run(runner);

        Expect.True(result.StoppedAtAssetPath?.EndsWith("tn_task.table.yml", StringComparison.Ordinal) == true,
            $"REQ-8: the result should name the table file whose create failed (tn_task.table.yml). Actual: <{result.StoppedAtAssetPath}>.");
        Expect.True(result.Failed >= 1, "REQ-8: the failed batch should be counted as a failure.");
        Expect.True(!result.StopWasDeclined, "REQ-8: a failure is not a decline.");
    }

    // As today (ImportSolutionCommand.TryRunAsync): a step that throws is a failure, not a crash - now for the batch too.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_RunnerThrowsDuringTheBatch_IsAFailureThatStopsLaterStages()
    {
        var runner = new ThrowingRunner();

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.True(result.Failed >= 1, "REQ-8: a create stage that threw should be counted as a failure.");
        Expect.Equal(0, runner.LaterStageCalls, "REQ-8: no later stage should run after the create stage threw.");
    }

    private sealed class ThrowingRunner : ISolutionImportStepRunner
    {
        public int LaterStageCalls { get; private set; }

        public Task<SolutionTableCreateResult> CreateTablesAsync(IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");

        public Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            if (stage == SolutionImportStage.CreateTable)
            {
                throw new InvalidOperationException("boom");
            }

            LaterStageCalls++;
            return Task.FromResult(SolutionImportStepOutcome.Succeeded);
        }

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName) => LaterStageCalls++;
    }

    // As today: a failing view or form doesn't stop the rest of the solution (only a failed table create does).
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task RunAsync_ViewImportFails_RemainingAssetsAreStillImported()
    {
        var runner = new RecordingRunner { Fails = (stage, file) => stage == SolutionImportStage.ImportView && file == "Active projects.view.yml" };

        var result = await Run(runner);

        Expect.Equal(1, result.Failed, "REQ-10: only the failed view should count as a failure.");
        Expect.Contains("ImportForm:Main.form.yml", string.Join(" | ", runner.Steps.Select(Describe)), "REQ-10: the form after the failed view should still be imported, as today.");
        Expect.Contains("ImportTable:tn_task.table.yml", string.Join(" | ", runner.Steps.Select(Describe)), "REQ-10: the next table folder should still be imported, as today.");
    }

    // REQ-9: "Saying no creates nothing and stops the run." - a declined batch stops exactly like a failed one, but says it was declined.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_BatchDeclined_StopsTheRunAsDeclinedWithNoLaterStage()
    {
        var runner = new RecordingRunner { CreateResult = _ => new SolutionTableCreateResult(SolutionImportStepOutcome.Declined, []) };

        var result = await Run(runner);

        Expect.Sequence(["CreateTables:tn_project.table.yml,tn_task.table.yml"], runner.Log, "REQ-9: a declined create should stop the run: no later stage may run.");
        Expect.True(result.StopWasDeclined, "REQ-9: the result should say the stop was a decline, not a Dataverse failure.");
        Expect.True(result.StoppedAtAssetPath is not null, "REQ-9: the result should be marked as stopped (StoppedAtAssetPath set) so the command exits non-zero, as it does today for a decline.");
        Expect.Equal(0, result.Skipped, "REQ-9: nothing is skipped; the run simply stopped.");
    }

    // "`--whatif` on solution import still skips would-be-created tables' later stages as today":
    // the runner says tn_project would be created, so its own table import, view and form have nothing live to preview against - skipped, not failed.
    // tn_task already exists and choices.yml belongs to no table: both preview as before.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task RunAsync_WhatIfTableWouldBeCreated_SkipsItsFolderAndPreviewsTheRest()
    {
        var runner = new RecordingRunner
        {
            CreateResult = paths => new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, [paths.Single(p => p.EndsWith("tn_project.table.yml", StringComparison.Ordinal))]),
        };

        var result = await Run(runner);

        Expect.Sequence(
            [
                "CreateTables:tn_project.table.yml,tn_task.table.yml",
                "ImportChoices:choices.yml",
                "ImportTable:tn_task.table.yml",
                "ImportView:All tasks.view.yml",
            ],
            runner.Log,
            "Only tn_project's later steps should be skipped; everything else still previews.");
        Expect.Sequence(
            [
                "ImportTable:tn_project.table.yml (tn_project)",
                "ImportView:Active projects.view.yml (tn_project)",
                "ImportForm:Main.form.yml (tn_project)",
            ],
            runner.Skipped,
            "Each of tn_project's later steps should be reported as skipped, naming the table.");
        Expect.Equal(0, result.Failed, "Skipped steps are not failures.");
        Expect.Equal(3, result.Skipped, "The result should count the skipped steps.");
        Expect.Equal(null, result.StoppedAtAssetPath, "A would-be-created table doesn't stop the run.");
    }

    // REQ-11 (older numbering, kept): "No stage rewrites local YAML files. Each `*.table.yml` is byte-identical before and after `solution import`."
    // The orchestrator only reads the folder; every file keeps its exact bytes whether the run succeeds or stops.
    [Theory]
    [Trait("Requirement", "REQ-11")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LeavesEveryFileInTheFolderByteIdentical(bool createFails)
    {
        var before = Snapshot();
        var runner = new RecordingRunner
        {
            CreateResult = paths => createFails ? new SolutionTableCreateResult(SolutionImportStepOutcome.Failed, [], paths[0]) : new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, []),
        };

        await Run(runner);

        var after = Snapshot();
        Expect.Sequence(before.Keys.OrderBy(k => k).ToList(), after.Keys.OrderBy(k => k).ToList(), "REQ-11: no file should be added or removed by solution import.");
        foreach (var (path, bytes) in before)
        {
            Expect.True(bytes.SequenceEqual(after[path]), $"REQ-11: '{path}' should be byte-identical before and after solution import.");
        }
    }

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(_root, p), File.ReadAllBytes);
}

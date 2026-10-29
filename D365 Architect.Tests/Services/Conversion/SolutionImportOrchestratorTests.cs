using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="SolutionImportOrchestrator"/> - the stage ordering of
/// <c>solution import</c> (REQ-10), that it never rewrites the folder it
/// reads (REQ-11) and that a failed table create stops the later stages
/// (REQ-12).
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
            return Task.FromResult(Fails(stage, fileName) ? SolutionImportStepOutcome.Failed : Outcome(stage, fileName));
        }

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName) =>
            Skipped.Add($"{stage}:{Path.GetFileName(assetPath)} ({tableLogicalName})");
    }

    private Task<SolutionImportResult> Run(RecordingRunner runner) => new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

    private static string Describe((SolutionImportStage Stage, string FileName) step) => $"{step.Stage}:{step.FileName}";

    // REQ-10: "first create any missing tables, then re-read live state, then import choices, columns, views and forms as today."
    // Example: folder with a new `tn_project` (columns, a view, a form) -> one `solution import` run ends with all of it live.
    // Every table's create step comes before any other kind of step.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task RunAsync_CreatesEveryTableBeforeAnyOtherStage()
    {
        var runner = new RecordingRunner();

        await Run(runner);

        var steps = runner.Steps.Select(Describe).ToList();
        var lastCreate = steps.FindLastIndex(s => s.StartsWith("CreateTable:", StringComparison.Ordinal));
        var firstOther = steps.FindIndex(s => !s.StartsWith("CreateTable:", StringComparison.Ordinal));
        Expect.True(lastCreate >= 0, $"REQ-10: each *.table.yml should get a CreateTable step. Steps: <{string.Join(" | ", steps)}>.");
        Expect.True(lastCreate < firstOther, $"REQ-10: every CreateTable step should run before choices, columns, views or forms. Steps: <{string.Join(" | ", steps)}>.");
    }

    // REQ-10: the whole sequence - creates for both tables, then the same order as before the change:
    // choices first, then per table folder (alphabetical) its table columns, views and forms.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task RunAsync_AfterTheCreates_FollowsTodaysOrder()
    {
        var runner = new RecordingRunner();

        await Run(runner);

        Expect.Sequence(
            [
                "CreateTable:tn_project.table.yml",
                "CreateTable:tn_task.table.yml",
                "ImportChoices:choices.yml",
                "ImportTable:tn_project.table.yml",
                "ImportView:Active projects.view.yml",
                "ImportForm:Main.form.yml",
                "ImportTable:tn_task.table.yml",
                "ImportView:All tasks.view.yml",
            ],
            runner.Steps.Select(Describe).ToList(),
            "REQ-10: solution import should create tables first, then import choices, then each table folder's columns, views and forms.");
    }

    // REQ-10 / REQ-6: a table that already exists is reported by the create step as a success, so the later stages still run.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task RunAsync_AllCreateStepsSucceed_ReportsNoFailures()
    {
        var runner = new RecordingRunner();

        var result = await Run(runner);

        Expect.Equal(0, result.Failed, "REQ-10: a run where every step succeeds should report no failures.");
        Expect.Equal(null, result.StoppedAtAssetPath, "REQ-10: a run that completed every stage should not report being stopped.");
    }

    // REQ-12: "If creating a table fails, later stages stop and say which table failed."
    // Example: Creation of `tn_project` rejected -> views/forms not attempted, error names `tn_project`.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task RunAsync_TableCreateFails_NoLaterStageIsAttempted()
    {
        var runner = new RecordingRunner { Fails = (stage, file) => stage == SolutionImportStage.CreateTable && file == "tn_project.table.yml" };

        await Run(runner);

        var attemptedLater = runner.Steps.Where(s => s.Stage != SolutionImportStage.CreateTable).Select(Describe).ToList();
        Expect.Empty(attemptedLater, "REQ-12: after tn_project's create failed, no choices, columns, views or forms should be attempted.");
    }

    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task RunAsync_TableCreateFails_ResultSaysWhichTableStoppedTheRun()
    {
        var runner = new RecordingRunner { Fails = (stage, file) => stage == SolutionImportStage.CreateTable && file == "tn_project.table.yml" };

        var result = await Run(runner);

        Expect.True(result.StoppedAtAssetPath?.EndsWith("tn_project.table.yml", StringComparison.Ordinal) == true,
            $"REQ-12: the result should name the table file whose create failed (tn_project.table.yml). Actual: <{result.StoppedAtAssetPath}>.");
        Expect.True(result.Failed >= 1, "REQ-12: the failed create should be counted as a failure.");
    }

    // As today (ImportSolutionCommand.TryRunAsync): a step that throws is a failure, not a crash.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task RunAsync_RunnerThrowsDuringCreate_IsAFailureThatStopsLaterStages()
    {
        var runner = new ThrowingRunner();

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.True(result.Failed >= 1, "REQ-12: a create step that threw should be counted as a failure.");
        Expect.Equal(0, runner.LaterStageCalls, "REQ-12: no later stage should run after a create step threw.");
    }

    private sealed class ThrowingRunner : ISolutionImportStepRunner
    {
        public int LaterStageCalls { get; private set; }

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

    // Declining a create at its prompt leaves the table missing, so it stops the run exactly like a failed create - but says it was declined.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task RunAsync_TableCreateDeclined_StopsTheRunAtThatTableAsDeclined()
    {
        var runner = new RecordingRunner
        {
            Outcome = (stage, file) => stage == SolutionImportStage.CreateTable && file == "tn_project.table.yml" ? SolutionImportStepOutcome.Declined : SolutionImportStepOutcome.Succeeded,
        };

        var result = await Run(runner);

        Expect.Sequence(["CreateTable:tn_project.table.yml"], runner.Steps.Select(Describe).ToList(), "A declined create should stop the run: no further creates and no later stages.");
        Expect.True(result.StoppedAtAssetPath?.EndsWith("tn_project.table.yml", StringComparison.Ordinal) == true, $"The result should name the declined table's file. Actual: <{result.StoppedAtAssetPath}>.");
        Expect.True(result.StopWasDeclined, "The result should say the stop was a decline, not a Dataverse failure.");
        Expect.Equal(1, result.Failed, "A declined create counts as the create not happening.");
    }

    // --whatif: tn_project would be created, so its own table import, view and form have nothing live to preview against - skipped, not failed.
    // tn_task already exists and choices.yml belongs to no table: both preview as before.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task RunAsync_WhatIfTableWouldBeCreated_SkipsItsFolderAndPreviewsTheRest()
    {
        var runner = new RecordingRunner
        {
            Outcome = (stage, file) => stage == SolutionImportStage.CreateTable && file == "tn_project.table.yml" ? SolutionImportStepOutcome.WouldCreate : SolutionImportStepOutcome.Succeeded,
        };

        var result = await Run(runner);

        Expect.Sequence(
            [
                "CreateTable:tn_project.table.yml",
                "CreateTable:tn_task.table.yml",
                "ImportChoices:choices.yml",
                "ImportTable:tn_task.table.yml",
                "ImportView:All tasks.view.yml",
            ],
            runner.Steps.Select(Describe).ToList(),
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

    // REQ-11: "No stage rewrites local YAML files. Each `*.table.yml` is byte-identical before and after `solution import`."
    // The orchestrator only reads the folder; every file keeps its exact bytes whether the run succeeds or stops.
    [Theory]
    [Trait("Requirement", "REQ-11")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LeavesEveryFileInTheFolderByteIdentical(bool createFails)
    {
        var before = Snapshot();
        var runner = new RecordingRunner { Fails = (stage, _) => createFails && stage == SolutionImportStage.CreateTable };

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

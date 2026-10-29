using D365Architect.Commands;
using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.Solution;

/// <summary>
/// Covers <see cref="ImportSolutionCommand.StepRunner"/> - the production
/// wiring between <see cref="SolutionImportOrchestrator"/>'s stages and the
/// per-asset commands. The ordering itself is pinned by
/// <c>SolutionImportOrchestratorTests</c>; here it's that the create stage
/// really runs <c>table create</c> with <c>solution import</c>'s own
/// <c>--solution</c>/<c>--yes</c>, and that a rejected create reaches the
/// orchestrator as the failure that stops the run.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportSolutionCommandStepRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportSolutionCommandStepRunnerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tn_project"));
        File.WriteAllText(TablePath, TableCreateFixtures.TableYaml());
    }

    private string TablePath => Path.Combine(_root, "tn_project", "tn_project.table.yml");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ImportSolutionCommand.StepRunner CreateRunner(FakeDataverseClient client, bool yes = true, bool whatIf = false) =>
        new(CreateCommand(client), Settings("unused", yes, whatIf));

    private static ImportSolutionCommand CreateCommand(FakeDataverseClient client) =>
        new(
            new TableCreateService(client),
            new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)),
            new ViewImportService(client),
            new FormImportService(client, new FormJsonDefinitionReader()),
            new GlobalChoiceImportService(client),
            new ImportRunner(new FakeAuthenticationService()));

    private static ImportSolutionCommand.Settings Settings(string input, bool yes, bool whatIf) =>
        new() { Input = input, Solution = "TNCore", Yes = yes, WhatIf = whatIf };

    /// <summary>A view and a form in tn_project's folder, so a run that wrongly carries on past its create would try (and fail) to import them.</summary>
    private void WriteViewAndForm()
    {
        File.WriteAllText(Path.Combine(_root, "tn_project", "Active projects.view.yml"), "name: Active projects\n");
        File.WriteAllText(Path.Combine(_root, "tn_project", "Main.form.yml"), "name: Main\n");
    }

    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task CreateTableStage_RunsTableCreateWithTheSolutionAndWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: --yes must be forwarded
        var client = new FakeDataverseClient { EntityExists = _ => false };

        var outcome = await CreateRunner(client).RunAsync(SolutionImportStage.CreateTable, TablePath, CancellationToken.None);

        Expect.True(outcome == SolutionImportStepOutcome.Succeeded, $"The create stage should succeed for a missing table. Output: <{console.Output}>");
        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal("TNCore", call.SolutionUniqueName, "REQ-8: solution import's --solution should reach the table create.");
    }

    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task RejectedCreate_StopsTheRunAtThatTableAndPrintsWhy()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient
        {
            EntityExists = _ => false,
            CreateEntityFailure = new HttpRequestException("Dataverse request failed (400 BadRequest): schema name is already in use"),
        };

        var result = await new SolutionImportOrchestrator().RunAsync(_root, CreateRunner(client), CancellationToken.None);

        Expect.Equal(TablePath, result.StoppedAtAssetPath, "REQ-12: the run should stop at tn_project.table.yml.");
        Expect.Contains("Creating table 'tn_project' failed", console.Output, "REQ-12: the create step should print which table failed.");
        Expect.Contains("schema name is already in use", console.Output, "REQ-12: and Dataverse's own reason.");
    }

    // Saying no at table create's prompt leaves the table missing: the run stops there, naming the table, in words distinct from a Dataverse rejection.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task DeclinedCreate_StopsTheRunNamingTheTable()
    {
        WriteViewAndForm();
        using var console = new ConsoleHarness("n");
        var client = new FakeDataverseClient { EntityExists = _ => false };

        var exitCode = await CreateCommand(client).RunAsync(Settings(_root, yes: false, whatIf: false), CancellationToken.None);

        Expect.Equal(1, exitCode, "A declined create stops the run like a failed one.");
        Expect.Contains("Stopped: creating the table in", console.Output, $"The stop should be reported as a decline. Output: <{console.Output}>");
        Expect.Contains("tn_project.table.yml' was declined", console.Output, "and name the declined table's file.");
        Expect.True(!console.Output.Contains("couldn't be created", StringComparison.Ordinal), "A decline isn't a Dataverse rejection and shouldn't be worded as one.");
        Expect.True(!console.Output.Contains("failed", StringComparison.Ordinal), $"Nothing after the declined create should have been attempted. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "Nothing should be created.");
    }

    // --whatif on a missing table: reported as would-be-created, its views/forms skipped with a note, and the run doesn't end "Done with errors".
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task WhatIf_MissingTable_SkipsItsLaterAssetsWithoutFailures()
    {
        WriteViewAndForm();
        using var console = new ConsoleHarness(); // --whatif never prompts
        var client = new FakeDataverseClient { EntityExists = _ => false };

        var exitCode = await CreateCommand(client).RunAsync(Settings(_root, yes: false, whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"A whatif run whose only gap is a table that would be created should succeed. Output: <{console.Output}>");
        Expect.Contains("'tn_project' would be created", console.Output, "The missing table should be reported as would-be-created.");
        Expect.Contains("Main.form.yml' skipped: 'tn_project' doesn't exist yet and would be created first.", console.Output, "Its form should be skipped with a note.");
        Expect.Contains("Active projects.view.yml' skipped: 'tn_project' doesn't exist yet", console.Output, "Its view should be skipped with a note.");
        Expect.True(!console.Output.Contains("Done with errors", StringComparison.Ordinal), $"Skipped assets aren't failures. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "--whatif writes nothing.");
    }

    // --whatif on a table that already exists: the create stage reports it and every later stage still previews, as before.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task WhatIf_ExistingTable_LaterStagesStillPreview()
    {
        WriteViewAndForm();
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient { EntityExists = _ => true };
        var runner = new CreateOnlyRunner(CreateRunner(client, yes: false, whatIf: true));

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, runner.CreateOutcome, $"An existing table's create stage succeeds. Output: <{console.Output}>");
        Expect.Contains("tn_project already exists, nothing to create.", console.Output, "The create stage should report the existing table.");
        Expect.Sequence(["ImportTable", "ImportView", "ImportForm"], runner.LaterStages, "Every later stage of an existing table should still run its preview.");
        Expect.Equal(0, result.Skipped, "Nothing should be skipped for a table that exists.");
    }

    /// <summary>Runs the create stage for real; records the later stages instead of running them (their own previews have their own tests).</summary>
    private sealed class CreateOnlyRunner(ImportSolutionCommand.StepRunner inner) : ISolutionImportStepRunner
    {
        public SolutionImportStepOutcome? CreateOutcome { get; private set; }

        public List<string> LaterStages { get; } = [];

        public async Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            if (stage != SolutionImportStage.CreateTable)
            {
                LaterStages.Add(stage.ToString());
                return SolutionImportStepOutcome.Succeeded;
            }

            CreateOutcome = await inner.RunAsync(stage, assetPath, cancellationToken);
            return CreateOutcome.Value;
        }

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName) => inner.ReportSkipped(stage, assetPath, tableLogicalName);
    }
}

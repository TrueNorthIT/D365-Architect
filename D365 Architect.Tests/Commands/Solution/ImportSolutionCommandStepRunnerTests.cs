using D365Architect.Commands;
using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.Solution;

/// <summary>
/// Covers <see cref="ImportSolutionCommand.StepRunner"/> - the production
/// wiring between <see cref="SolutionImportOrchestrator"/>'s stages and the
/// per-asset commands. The ordering itself is pinned by
/// <c>SolutionImportOrchestratorTests</c>; here it is the create stage.
///
/// Batch table creation (the "create tables in one transaction" behaviour,
/// tagged with the "Feature" trait "batch-table-create"): <see cref="ISolutionImportStepRunner.CreateTablesAsync"/>
/// previews every table, lists the ones that don't exist, asks ONCE and creates them all in one
/// Dataverse transaction (a single <c>$batch</c> changeset, which the fake records as one
/// <see cref="FakeDataverseClient.ExecuteTransactionCalls"/> entry of
/// <see cref="DataverseWrite.CreateEntity"/> writes).
///
/// Sits in the "Console" collection because it swaps the process-wide <c>AnsiConsole.Console</c>;
/// a prompt nobody answered fails the test, which is how a single prompt is pinned.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportSolutionCommandStepRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportSolutionCommandStepRunnerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tn_project"));
        Directory.CreateDirectory(Path.Combine(_root, "tn_task"));
        File.WriteAllText(TablePath, TableCreateFixtures.TableYaml());
        File.WriteAllText(TaskTablePath, TaskTableYaml());
    }

    private string TablePath => Path.Combine(_root, "tn_project", "tn_project.table.yml");
    private string TaskTablePath => Path.Combine(_root, "tn_task", "tn_task.table.yml");

    /// <summary>The same valid table as <see cref="TableCreateFixtures.TableYaml"/>, under a second name.</summary>
    private static string TaskTableYaml(bool includeOwnership = true) =>
        TableCreateFixtures.TableYaml(includeOwnership: includeOwnership).Replace("tn_project", "tn_task").Replace("tn_Project", "tn_Task");

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

    /// <summary>A fake where only the named tables exist live.</summary>
    private static FakeDataverseClient ClientWhereExist(params string[] liveTables) =>
        new() { EntityExists = name => liveTables.Contains(name) };

    private IReadOnlyList<string> BothTables => [TablePath, TaskTablePath];

    private static List<DataverseWrite.CreateEntity> CreateWrites(FakeDataverseClient client) =>
        client.ExecuteTransactionCalls.SelectMany(c => c).OfType<DataverseWrite.CreateEntity>().ToList();

    /// <summary>A view and a form in tn_project's folder, so a run that wrongly carries on past its create would try (and fail) to import them.</summary>
    private void WriteViewAndForm()
    {
        File.WriteAllText(Path.Combine(_root, "tn_project", "Active projects.view.yml"), "name: Active projects\n");
        File.WriteAllText(Path.Combine(_root, "tn_project", "Main.form.yml"), "name: Main\n");
    }

    // ---- one list, one question ----

    // "`solution import` lists every table it would create and asks once ("Create these N tables?")."
    // Code path: ImportSolutionCommand.StepRunner.CreateTablesAsync: previews each table, prints the ones to create, one AnsiConsole.Confirm.
    // The harness has ONE answer, so a second prompt would fail the test.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_TwoMissingTables_ListsBothAndAsksOnce()
    {
        using var console = new ConsoleHarness("y");
        var client = ClientWhereExist();

        var result = await CreateRunner(client, yes: false).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, result.Outcome, $"confirming once should create the tables. Output: <{console.Output}>");
        Expect.Contains("Create these 2 tables?", console.Output, "the question should be 'Create these 2 tables?' for two missing tables.");
        Expect.Contains("tn_project", console.Output, "the list should name the first table it would create.");
        Expect.Contains("tn_task", console.Output, "the list should name the second table it would create.");
    }

    // A table that already exists isn't "to create": it is neither listed in the count nor sent.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_OneTableAlreadyExists_OnlyTheMissingOneIsCreated()
    {
        using var console = new ConsoleHarness(); // --yes
        var client = ClientWhereExist("tn_project");

        var result = await CreateRunner(client).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, result.Outcome, $"a run with one existing table should succeed. Output: <{console.Output}>");
        var writes = CreateWrites(client);
        Expect.Sequence(["tn_Task"], writes.Select(w => (string?)w.Metadata["SchemaName"] ?? "").ToList(), "only the table that doesn't exist (tn_task) should be created.");
    }

    // The one question before creating the tables, and "`--yes` still skips prompts".
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_WithYes_CreatesWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: a prompt would fail the test
        var client = ClientWhereExist();

        var result = await CreateRunner(client, yes: true).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, result.Outcome, $"--yes should create both tables without asking. Output: <{console.Output}>");
        Expect.Equal(2, CreateWrites(client).Count, "--yes should still create both tables.");
    }

    // When nothing is missing there is nothing to ask and nothing to write.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_EveryTableAlreadyExists_AsksNothingAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // no answers
        var client = ClientWhereExist("tn_project", "tn_task");

        var result = await CreateRunner(client, yes: false).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, result.Outcome, $"Nothing to create should be a success. Output: <{console.Output}>");
        Expect.Empty(client.ExecuteTransactionCalls, "Nothing should be sent when every table already exists.");
        Expect.Empty(result.WouldCreateAssetPaths, "Nothing would be created, so no folder should be skipped.");
    }

    // ---- one transaction ----

    // "All new tables are created in one transaction (a single Web API $batch changeset of POST EntityDefinitions requests)."
    // Code path: StepRunner.CreateTablesAsync -> TableCreateService.ApplyAllAsync -> IDataverseClient.ExecuteTransactionAsync([CreateEntity, CreateEntity]).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_TwoMissingTables_AreCreatedInOneTransactionInFileOrder()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereExist();

        await CreateRunner(client).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(1, client.ExecuteTransactionCalls.Count, $"both creates should be sent in ONE transaction. Output: <{console.Output}>");
        var writes = client.ExecuteTransactionCalls.FirstOrDefault() ?? [];
        Expect.Sequence(["tn_Project", "tn_Task"], writes.OfType<DataverseWrite.CreateEntity>().Select(w => (string?)w.Metadata["SchemaName"] ?? "").ToList(),
            "the transaction should hold one CreateEntity (POST EntityDefinitions) per new table, in file order, and nothing else.");
        Expect.Equal(2, writes.Count, "the transaction should hold only the two table creates.");
    }

    // "If any fails, none are created ... and the error names the table that failed" - Dataverse names the failing request by Content-ID; here request 2 = tn_task.
    // Code path: TableCreateService.ApplyAllAsync maps DataverseTransactionFailedException.FailedContentId back to the table; StepRunner reports that table's file.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_DataverseRollsBackNamingRequestTwo_FailsNamingTheSecondTableAndCreatesNothing()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereExist();
        client.TransactionFailure = (2, "Dataverse transaction rolled back: schema name tn_Task is already in use");

        var result = await CreateRunner(client).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Failed, result.Outcome, $"a rolled-back transaction is a failed create stage. Output: <{console.Output}>");
        Expect.Equal(TaskTablePath, result.FailedAssetPath, "the failure should be attributed to the table whose request Dataverse named (Content-ID 2 = tn_task).");
        Expect.Contains("tn_task", console.Output, "the printed error should name the table that failed.");
        Expect.Contains("already in use", console.Output, "the printed error should keep Dataverse's own reason.");
        Expect.Empty(client.CreateEntityCalls, "nothing should have been created.");
    }

    // A file that can't be used (here no ownershipType) fails the whole stage BEFORE anything is sent: none are created, not even the valid one.
    // Code path: TableCreateService.PreviewAsync throws TableCreateValidationException; StepRunner.CreateTablesAsync reports Failed for that file.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_OneTableFileIsInvalid_FailsNamingItAndCreatesNothing()
    {
        File.WriteAllText(TaskTablePath, TaskTableYaml(includeOwnership: false));
        using var console = new ConsoleHarness();
        var client = ClientWhereExist();

        var result = await CreateRunner(client).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Failed, result.Outcome, $"one unusable table file should fail the create stage. Output: <{console.Output}>");
        Expect.Equal(TaskTablePath, result.FailedAssetPath, "the failure should name the file that couldn't be used.");
        Expect.Contains("ownershipType is required to create tn_task", console.Output, "the user should be told what is wrong with tn_task.");
        Expect.Empty(client.ExecuteTransactionCalls, "not even the valid table should be sent when another can't be created.");
        Expect.Empty(client.CreateEntityCalls, "nothing should have been created.");
    }

    // ---- no means no ----

    // "Saying no creates nothing and stops the run." (the runner's half: the outcome the orchestrator stops on)
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_AnsweringNo_CreatesNothingAndReportsDeclined()
    {
        using var console = new ConsoleHarness("n");
        var client = ClientWhereExist();

        var result = await CreateRunner(client, yes: false).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Declined, result.Outcome, $"answering no should report the create stage as declined. Output: <{console.Output}>");
        Expect.Empty(client.ExecuteTransactionCalls, "answering no should send nothing.");
        Expect.Empty(client.CreateEntityCalls, "answering no should create no table.");
    }

    // Declining the create, end to end: the run stops - exit 1, said to be declined, no view or form touched, and still only the one question (the harness has one answer).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task SolutionImport_AnsweringNo_StopsTheRunAndCreatesNothing()
    {
        WriteViewAndForm();
        using var console = new ConsoleHarness("n");
        var client = ClientWhereExist();

        var exitCode = await CreateCommand(client).RunAsync(Settings(_root, yes: false, whatIf: false), CancellationToken.None);

        Expect.Equal(1, exitCode, $"declining the create should stop the run with exit code 1, as today. Output: <{console.Output}>");
        Expect.Contains("declined", console.Output, "the stop should be reported as a decline.");
        Expect.True(!console.Output.Contains("failed", StringComparison.Ordinal), $"nothing after the declined create should have been attempted. Output: <{console.Output}>");
        Expect.Empty(client.ExecuteTransactionCalls, "nothing should be created.");
        Expect.Empty(client.CreateEntityCalls, "nothing should be created.");
    }

    // ---- still added to --solution ----

    // "Tables created together are still added to `--solution`" - the MSCRM.SolutionUniqueName header on each request inside the changeset
    // (DataverseWrite.CreateEntity.SolutionUniqueName; DataverseClient sends it per request - see DataverseClientBatchCreateTests).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_EveryCreateInTheTransactionCarriesTheSolution()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereExist();

        await CreateRunner(client).CreateTablesAsync(BothTables, CancellationToken.None);

        var writes = CreateWrites(client);
        Expect.Equal(2, writes.Count, $"both tables should be created. Output: <{console.Output}>");
        Expect.True(writes.All(w => w.SolutionUniqueName == "TNCore"), $"solution import's --solution (TNCore) should reach every create. Actual: <{string.Join(", ", writes.Select(w => w.SolutionUniqueName ?? "null"))}>.");
    }

    // ---- --whatif ----

    // "`--whatif` on solution import still skips would-be-created tables' later stages as today": the runner reports which tables would be created
    // (so the orchestrator can skip their folders), lists them, never prompts and writes nothing.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task CreateTables_WhatIf_ListsAndReportsTheTablesThatWouldBeCreatedAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var client = ClientWhereExist("tn_task");

        var result = await CreateRunner(client, yes: false, whatIf: true).CreateTablesAsync(BothTables, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, result.Outcome, $"--whatif should succeed. Output: <{console.Output}>");
        Expect.Sequence([TablePath], result.WouldCreateAssetPaths, "--whatif should report the table that doesn't exist (tn_project) as would-be-created, and not the one that does.");
        Expect.Contains("tn_project", console.Output, "--whatif should list the table it would create.");
        Expect.Empty(client.ExecuteTransactionCalls, "--whatif must not write.");
    }

    // ---- as before ----

    // As before the batch: a rejected create (here a plain error with no Content-ID, from a single table) stops the run at that table and prints why.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task RejectedCreate_OfASingleTable_StopsTheRunAtThatTableAndPrintsWhy()
    {
        File.Delete(TaskTablePath);
        Directory.Delete(Path.GetDirectoryName(TaskTablePath)!);
        using var console = new ConsoleHarness();
        var client = ClientWhereExist();
        client.CreateEntityFailure = new HttpRequestException("Dataverse request failed (400 BadRequest): schema name is already in use");

        var result = await new SolutionImportOrchestrator().RunAsync(_root, CreateRunner(client), CancellationToken.None);

        Expect.Equal(TablePath, result.StoppedAtAssetPath, "the run should stop at tn_project.table.yml.");
        Expect.Contains("Creating table 'tn_project' failed", console.Output, "the create step should print which table failed.");
        Expect.Contains("schema name is already in use", console.Output, "and Dataverse's own reason.");
    }

    // A failed transaction, end to end: a rolled-back batch stops the whole run - exit 1, the failing table named, and no choice, column, view or form attempted.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task SolutionImport_BatchRolledBack_StopsBeforeColumnsViewsAndFormsNamingTheTable()
    {
        WriteViewAndForm();
        using var console = new ConsoleHarness();
        var client = ClientWhereExist();
        client.TransactionFailure = (2, "schema name tn_Task is already in use");

        var exitCode = await CreateCommand(client).RunAsync(Settings(_root, yes: true, whatIf: false), CancellationToken.None);

        Expect.Equal(1, exitCode, $"a failed create should stop the run with exit code 1. Output: <{console.Output}>");
        Expect.Contains("tn_task", console.Output, "the error should name the table that failed.");
        Expect.Contains("already in use", console.Output, "the error should keep Dataverse's own reason.");
        Expect.Empty(client.EntityIdentityCalls, "no view or form should be attempted after the create stage failed (they look the table up first).");
        Expect.Empty(client.CreateEntityCalls, "no table should have been created.");
    }

    // --whatif on a missing table: reported as would-be-created, its views/forms skipped with a note, and the run doesn't end "Done with errors".
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task WhatIf_MissingTable_SkipsItsLaterAssetsWithoutFailures()
    {
        File.Delete(TaskTablePath);
        Directory.Delete(Path.GetDirectoryName(TaskTablePath)!);
        WriteViewAndForm();
        using var console = new ConsoleHarness(); // --whatif never prompts
        var client = ClientWhereExist();

        var exitCode = await CreateCommand(client).RunAsync(Settings(_root, yes: false, whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"A whatif run whose only gap is a table that would be created should succeed. Output: <{console.Output}>");
        Expect.Contains("'tn_project' would be created", console.Output, "The missing table should be reported as would-be-created.");
        Expect.Contains("Main.form.yml' skipped: 'tn_project' doesn't exist yet and would be created first.", console.Output, "Its form should be skipped with a note.");
        Expect.Contains("Active projects.view.yml' skipped: 'tn_project' doesn't exist yet", console.Output, "Its view should be skipped with a note.");
        Expect.True(!console.Output.Contains("Done with errors", StringComparison.Ordinal), $"Skipped assets aren't failures. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "--whatif writes nothing.");
    }

    // --whatif on a table that already exists: the create stage reports nothing to create and every later stage still previews, as before.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task WhatIf_ExistingTable_LaterStagesStillPreview()
    {
        File.Delete(TaskTablePath);
        Directory.Delete(Path.GetDirectoryName(TaskTablePath)!);
        WriteViewAndForm();
        using var console = new ConsoleHarness();
        var client = ClientWhereExist("tn_project");
        var runner = new CreateOnlyRunner(CreateRunner(client, yes: false, whatIf: true));

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Equal(1, runner.CreateBatches, $"The create stage should be one CreateTablesAsync call. Output: <{console.Output}>");
        Expect.Equal(SolutionImportStepOutcome.Succeeded, runner.CreateOutcome, $"An existing table's create stage succeeds. Output: <{console.Output}>");
        Expect.Sequence(["ImportTable", "ImportView", "ImportForm"], runner.LaterStages, "Every later stage of an existing table should still run its preview.");
        Expect.Equal(0, result.Skipped, "Nothing should be skipped for a table that exists.");
    }

    /// <summary>Runs the create stage for real; records the later stages instead of running them (their own previews have their own tests).</summary>
    private sealed class CreateOnlyRunner(ImportSolutionCommand.StepRunner inner) : ISolutionImportStepRunner
    {
        public SolutionImportStepOutcome? CreateOutcome { get; private set; }

        public int CreateBatches { get; private set; }

        public List<string> LaterStages { get; } = [];

        public async Task<SolutionTableCreateResult> CreateTablesAsync(IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken)
        {
            CreateBatches++;
            var result = await inner.CreateTablesAsync(tableAssetPaths, cancellationToken);
            CreateOutcome = result.Outcome;
            return result;
        }

        public Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            if (stage != SolutionImportStage.CreateTable)
            {
                LaterStages.Add(stage.ToString());
            }

            return Task.FromResult(SolutionImportStepOutcome.Succeeded);
        }

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName) => inner.ReportSkipped(stage, assetPath, tableLogicalName);
    }
}

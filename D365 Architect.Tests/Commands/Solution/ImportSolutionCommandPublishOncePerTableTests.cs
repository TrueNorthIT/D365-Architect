using D365Architect.Commands;
using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.RichTextAndAutoNumberFixtures;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Commands.Solution;

/// <summary>
/// "<c>solution import</c> publishes each table that had changes applied once, after all of its changes are in"
/// (the "publish once per table" behaviour, tagged with the "Feature" trait "publish-once-per-table").
/// Whole runs of <see cref="ImportSolutionCommand.RunAsync"/> over a solution folder with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked. The tables already exist live, so there is
/// no create stage; the views are changed by <c>savedQueryId</c> so each one can be changed, unchanged or stale on its own.
///
/// What a table's publish costs is one <c>PublishXml</c> call, so the counts below are the behaviour: today every changed
/// view, form and table import publishes its table straight after its own apply, so a table with five changed views is
/// published five times. The decision is to publish at the end of the run, after every apply
/// (<see cref="FakeDataverseClient.WritesBeforeEachEntityPublish"/> reads that).
///
/// The single-file commands (<c>view import</c>, <c>form import</c>, <c>table import</c>, <c>choice import</c>) keep publishing straight
/// after their own apply; <c>ImportViewCommandPublishTests</c>, <c>ImportFormCommandPublishTests</c>,
/// <c>ImportTableCommandPublishTests</c> and <c>ImportChoiceCommandPublishTests</c> pin that and must keep passing.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered fails the test.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportSolutionCommandPublishOncePerTableTests : IDisposable
{
    private const string Solution = "TNCore";
    private const string OldFetchXml = "<fetch>old</fetch>";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));
    private int _viewCount;

    public ImportSolutionCommandPublishOncePerTableTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ImportSolutionCommand CreateCommand(FakeDataverseClient client) =>
        new(
            new TableCreateService(client),
            new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)),
            new ViewImportService(client),
            new FormImportService(client, new FormJsonDefinitionReader()),
            new GlobalChoiceImportService(client),
            new SecurityImportService(client),
            new ImportRunner(new FakeAuthenticationService()));

    private Task<int> RunSolutionImport(FakeDataverseClient client, bool yes = true, bool whatIf = false, bool noPublish = false) =>
        CreateCommand(client).RunAsync(
            new ImportSolutionCommand.Settings { Input = _root, Solution = Solution, Yes = yes, WhatIf = whatIf, NoPublish = noPublish },
            CancellationToken.None);

    /// <summary>A fake where every table exists live (so nothing is created).</summary>
    private static FakeDataverseClient ClientWhereTablesExist() => new() { EntityExists = _ => true };

    private static string FetchXmlFor(string table) =>
        $"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"{table}\"><attribute name=\"{table}id\" /></entity></fetch>";

    private string TableFolder(string table)
    {
        var folder = Path.Combine(_root, table);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Writes a view file for <paramref name="table"/> and returns its id; the live view is added by the caller.</summary>
    private Guid WriteView(string table)
    {
        _viewCount++;
        var id = new Guid($"00000000-0000-0000-0000-{_viewCount:D12}");
        var name = $"{table} view {_viewCount}";
        File.WriteAllText(
            Path.Combine(TableFolder(table), $"{name}.view.yml"),
            ViewYamlSerializer.ToYaml(ViewOf(name: name, entity: table, fetchXml: FetchXmlFor(table), savedQueryId: id)));
        return id;
    }

    /// <summary><paramref name="count"/> view files on the table, each differing from its live view, so each is updated.</summary>
    private void AddChangedViews(FakeDataverseClient client, string table, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var id = WriteView(table);
            client.SavedQueriesById[id] = new ExistingSavedQuery(id, null, OldFetchXml, null);
        }
    }

    /// <summary>A view file identical to its live view: nothing to apply.</summary>
    private void AddUnchangedView(FakeDataverseClient client, string table)
    {
        var id = WriteView(table);
        client.SavedQueriesById[id] = new ExistingSavedQuery(id, null, FetchXmlFor(table), null);
    }

    /// <summary>A view file whose <c>savedQueryId</c> matches no live view: the preview refuses it, so it is never applied.</summary>
    private void AddStaleView(string table) => WriteView(table);

    private static List<string> Sorted(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal).ToList();

    // ---- REQ-1: each table with changes applied is published once ----

    // "Example: 3 changed views on table A, 5 on table B: two publishes, not eight."
    // Code path: ImportSolutionCommand.RunAsync -> StepRunner.ConfirmAndApplyAsync -> SolutionImportFlow applies each view; the tables are published once between them, not by every ImportRunner apply.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-1")]
    public async Task SolutionImport_ThreeChangedViewsOnTableAAndFiveOnTableB_PublishesEachTableOnce()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 3);
        AddChangedViews(client, "tn_beta", 5);

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(8, client.UpdateSavedQueryCalls.Count, "all eight changed views should still be updated.");
        Expect.Equal(2, client.PublishEntityCalls.Count, $"3 changed views on tn_alpha and 5 on tn_beta should publish two tables, once each, not once per view. Published: <{string.Join(", ", client.PublishEntityCalls)}>.");
        Expect.Sequence(["tn_alpha", "tn_beta"], Sorted(client.PublishEntityCalls), "each table with applied changes should be published exactly once.");
    }

    // The publish is per table, whichever kind of asset changed it: a changed view and a created form on one table are one publish.
    // Code path: ViewImportService.PublishAsync and FormImportService.PublishAsync both reach IDataverseClient.PublishEntityAsync(table).
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-1")]
    public async Task SolutionImport_ChangedViewAndNewFormOnOneTable_PublishesTheTableOnce()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        client.EntityIdentities["tn_alpha"] = new EntityIdentity(10722, "tn_alphaid", "tn_name");
        AddChangedViews(client, "tn_alpha", 1);
        File.WriteAllText(Path.Combine(TableFolder("tn_alpha"), "Alpha.form.yml"), FormYamlSerializer.ToYaml(FormOf("Alpha", entity: "tn_alpha")));

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "the changed view should be updated.");
        Expect.Equal(1, client.CreateSystemFormCalls.Count, "the new form should be created.");
        Expect.Sequence(["tn_alpha"], client.PublishEntityCalls, "a changed view and a new form on one table should publish the table once.");
    }

    // The same for a table's own import: its new column and its changed view are one publish of the table.
    // Code path: TableImportService.PublishAsync and ViewImportService.PublishAsync both reach IDataverseClient.PublishEntityAsync(table).
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-1")]
    public async Task SolutionImport_ChangedColumnAndChangedViewOnOneTable_PublishesTheTableOnce()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        client.EntityDefinitionJson = Entity(); // the live table lacks the column below, so the table import adds it
        client.EntityMetadataJson = "{}";
        var column = AttributeDefinitionBuilder.Create("String", AutoNumberSchemaName, b =>
        {
            b.Name = AutoNumberLogicalName;
            b.AutoNumberFormat = "MSUM{SEQNUM:6}";
        });
        File.WriteAllText(
            Path.Combine(TableFolder("tn_test"), "tn_test.table.yml"),
            EntityYamlSerializer.ToYaml(new EntityDefinition { LogicalName = "tn_test", Attributes = [column] }));
        AddChangedViews(client, "tn_test", 1);

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateAttributeCalls.Count, "the new column should be created.");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "the changed view should be updated.");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "a new column and a changed view on one table should publish the table once.");
    }

    // "Decision: publish at the end of the run, after every apply." - when each table is published, every write in the run has been made.
    // Code path: the publishes happen after SolutionImportFlow.ConfirmAndApplyAsync has applied the last change; FakeDataverseClient.WritesBeforeEachEntityPublish holds the write count at each publish.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-1")]
    public async Task SolutionImport_PublishesOnlyAfterEveryChangeInTheRunIsApplied()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 2);
        AddChangedViews(client, "tn_beta", 2);

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(4, client.WriteOrder.Count, "all four changed views should be written.");
        Expect.True(client.WritesBeforeEachEntityPublish.Count > 0, "the changed tables should be published.");
        Expect.True(
            client.WritesBeforeEachEntityPublish.All(writes => writes == 4),
            $"every publish should come after all four writes, at the end of the run. Writes made before each publish, in order: <{string.Join(", ", client.WritesBeforeEachEntityPublish)}>.");
    }

    // ---- REQ-2: a table where nothing was applied is not published ----

    // "A table where nothing was applied is not published (all its files unchanged or failed)." - tn_beta's only view already matches live.
    // Code path: only tables with at least one applied change reach the end-of-run publish.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-2")]
    public async Task SolutionImport_TableWhoseFilesAreAllUnchanged_IsNotPublished()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 2);
        AddUnchangedView(client, "tn_beta");

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(0, exitCode, $"an unchanged view is not an error. Output: <{console.Output}>");
        Expect.Equal(2, client.UpdateSavedQueryCalls.Count, "only tn_alpha's two changed views should be updated.");
        Expect.Sequence(["tn_alpha"], client.PublishEntityCalls, "tn_alpha had changes applied and tn_beta had none, so only tn_alpha should be published, once.");
    }

    // The same when the table's files failed: a stale savedQueryId is refused at preview and never applied.
    // Code path: ViewImportService.PreviewAsync throws ViewNotFoundException; the file counts as failed and is never applied.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-2")]
    public async Task SolutionImport_TableWhoseOnlyFileFails_IsNotPublished()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 2);
        AddStaleView("tn_beta");

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(1, exitCode, $"a refused view file should make the run exit 1. Output: <{console.Output}>");
        Expect.Equal(2, client.UpdateSavedQueryCalls.Count, "tn_alpha's two changed views should still be updated.");
        Expect.Sequence(["tn_alpha"], client.PublishEntityCalls, "tn_beta's only file failed and nothing was applied to it, so only tn_alpha should be published, once.");
    }

    // "A table with one applied and one failed file is published": the failed file doesn't hold back the change that went in.
    // Code path: the applied view's publish is collected; the stale view never gets past preview.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-2")]
    public async Task SolutionImport_TableWithOneAppliedAndOneFailedFile_IsPublishedOnce()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 1);
        AddStaleView("tn_alpha");

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(1, exitCode, $"the refused view file should make the run exit 1. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "the changed view should still be updated.");
        Expect.Sequence(["tn_alpha"], client.PublishEntityCalls, "tn_alpha had a change applied, so it should be published once despite its failed file.");
    }

    // ---- a table created in the run's create stage is published once, at the end, with the rest ----

    private static ImportSolutionCommand.StepRunner CreateRunner(FakeDataverseClient client, bool noPublish = false) =>
        new(CreateCommand(client), new ImportSolutionCommand.Settings { Input = "unused", Solution = Solution, Yes = true, NoPublish = noPublish });

    private string WriteCreatableTable()
    {
        var path = Path.Combine(TableFolder("tn_project"), "tn_project.table.yml");
        File.WriteAllText(path, TableCreateFixtures.TableYaml());
        return path;
    }

    // The create stage used to publish nothing itself, so a created table with no other change was left unpublished.
    // Code path: StepRunner.CreateTablesAsync hands each created table to SolutionImportFlow.AddPublish; ConfirmAndApplyAsync publishes it.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-1")]
    public async Task CreatedTableWithNoOtherChange_IsPublishedOnceAtTheEnd()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient { EntityExists = _ => false };
        var runner = CreateRunner(client);

        await runner.CreateTablesAsync([WriteCreatableTable()], CancellationToken.None);
        Expect.Empty(client.PublishEntityCalls, "nothing should be published until the run's changes are all applied.");
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "the created table should be published once.");
    }

    // Code path: the create stage's publish and the view's publish share the target tn_project, so SolutionImportFlow runs one of them.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-1")]
    public async Task CreatedTableWithAChangedView_IsPublishedOnceAfterTheView()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient { EntityExists = _ => false };
        var runner = CreateRunner(client);
        var tablePath = WriteCreatableTable();
        AddChangedViews(client, "tn_project", 1);
        var viewPath = Directory.GetFiles(TableFolder("tn_project"), "*.view.yml").Single();

        await runner.CreateTablesAsync([tablePath], CancellationToken.None);
        await runner.RunAsync(SolutionImportStage.ImportView, viewPath, CancellationToken.None);
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "the changed view should be updated.");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "a created table with a changed view should be published once.");
        Expect.Equal(client.WriteOrder.Count, client.WritesBeforeEachEntityPublish.Single(), "the publish should come after the view update.");
    }

    // Code path: CreateTablesAsync adds no publish under --no-publish.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-3")]
    public async Task CreatedTable_WithNoPublish_IsNotPublished()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient { EntityExists = _ => false };
        var runner = CreateRunner(client, noPublish: true);

        await runner.CreateTablesAsync([WriteCreatableTable()], CancellationToken.None);
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.ExecuteTransactionCalls.Count, "the table should still be created.");
        PublishAssertions.NothingPublished(client, "--no-publish should leave the created table unpublished.");
    }

    // ---- REQ-3: --no-publish, --whatif and declining still publish nothing ----

    // Code path: ImportSolutionCommand.Settings.NoPublish reaches every per-asset command, and so the end-of-run publish too.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-3")]
    public async Task SolutionImport_WithNoPublish_AppliesTheChangesAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 2);
        AddChangedViews(client, "tn_beta", 2);

        var exitCode = await RunSolutionImport(client, noPublish: true);

        Expect.Equal(0, exitCode, $"--no-publish should still import. Output: <{console.Output}>");
        Expect.Equal(4, client.UpdateSavedQueryCalls.Count, "--no-publish should still write every change.");
        PublishAssertions.NothingPublished(client, "'solution import --no-publish' should publish nothing.");
    }

    // Code path: SolutionImportFlow.ConfirmAndApplyAsync under whatIf returns before applying anything.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-3")]
    public async Task SolutionImport_WithWhatIf_WritesAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 2);
        AddChangedViews(client, "tn_beta", 2);

        var exitCode = await RunSolutionImport(client, yes: false, whatIf: true);

        Expect.Equal(0, exitCode, $"--whatif should succeed. Output: <{console.Output}>");
        Expect.Empty(client.UpdateSavedQueryCalls, "--whatif must not write.");
        PublishAssertions.NothingPublished(client, "'solution import --whatif' never publishes.");
    }

    // Code path: SolutionImportFlow.ConfirmAndApplyAsync returns after "Aborted." when the prompt is declined.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-3")]
    public async Task SolutionImport_DecliningThePrompt_WritesAndPublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var client = ClientWhereTablesExist();
        AddChangedViews(client, "tn_alpha", 2);
        AddChangedViews(client, "tn_beta", 2);

        var exitCode = await RunSolutionImport(client, yes: false);

        Expect.Equal(0, exitCode, $"declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.UpdateSavedQueryCalls, "declining should write nothing.");
        PublishAssertions.NothingPublished(client, "declining applied nothing, so nothing should be published.");
    }

    // ---- REQ-5: every global choice changed in the run is published in one call ----

    // "All global choices changed in the run are published in one call (one PublishGlobalOptionSetsAsync with all names)."
    // Code path: ImportChoiceCommand's PublishAsync -> GlobalChoiceImportService.PublishAsync -> IDataverseClient.PublishGlobalOptionSetsAsync(names).
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-5")]
    public async Task SolutionImport_TwoNewChoices_PublishesBothInOneCall()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        client.GlobalOptionSetJsonByName = _ => null; // neither choice is live: the import creates both
        File.WriteAllText(Path.Combine(_root, "choices.yml"), GlobalChoiceYamlSerializer.ToYaml([Choice("bogdan_status"), Choice("bogdan_stage")]));

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(2, client.CreateGlobalOptionSetCalls.Count, "both new choices should be created.");
        Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "both choices should be published in a single PublishXml call.");
        Expect.Sequence(["bogdan_stage", "bogdan_status"], Sorted(client.PublishGlobalOptionSetsCalls.FirstOrDefault() ?? []), "the one call should name every choice the run changed.");
        Expect.Empty(client.PublishEntityCalls, "publishing choices should not publish any table.");
    }

    // The choices come first in a run, but like the tables they are published once every change is in.
    // Code path: the choice publish moves from straight after the choices.yml apply to the end of the run.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-5")]
    public async Task SolutionImport_ChoicesAndViews_PublishesTheChoicesInOneCallAfterEveryChangeIsApplied()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        client.GlobalOptionSetJsonByName = _ => null;
        File.WriteAllText(Path.Combine(_root, "choices.yml"), GlobalChoiceYamlSerializer.ToYaml([Choice("bogdan_status"), Choice("bogdan_stage")]));
        AddChangedViews(client, "tn_alpha", 2);

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(0, exitCode, $"the run should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "both choices should be published in a single PublishXml call.");
        Expect.Equal(2, client.WritesBeforeEachGlobalOptionSetsPublish.FirstOrDefault(), $"the choices should be published after both view updates, at the end of the run. Writes made before the choice publish: <{client.WritesBeforeEachGlobalOptionSetsPublish.FirstOrDefault()}>.");
    }

    // ---- REQ-6: one table's publish failing does not stop the others ----

    // "If one table's publish fails, the error is shown, the other tables still publish, and the exit code is 1."
    // Code path: the end-of-run publish of each table catches that table's failure, prints it and carries on with the next table.
    [Fact]
    [Trait("Feature", "publish-once-per-table")]
    [Trait("Requirement", "REQ-6")]
    public async Task SolutionImport_OneTablesPublishFails_ShowsTheErrorPublishesTheOthersAndExitsOne()
    {
        using var console = new ConsoleHarness();
        var client = ClientWhereTablesExist();
        client.PublishEntityFailure = table => table == "tn_beta"
            ? new HttpRequestException("Dataverse request failed (400 BadRequest): PublishXml rejected tn_beta")
            : null;
        AddChangedViews(client, "tn_alpha", 2);
        AddChangedViews(client, "tn_beta", 2);
        AddChangedViews(client, "tn_gamma", 2);

        var exitCode = await RunSolutionImport(client);

        Expect.Equal(1, exitCode, $"a failed publish should make the run exit 1. Output: <{console.Output}>");
        Expect.Equal(6, client.UpdateSavedQueryCalls.Count, "every changed view should still have been updated before any publish.");
        Expect.Contains("PublishXml rejected tn_beta", console.Output, "the publish failure should be shown to the user.");
        Expect.Sequence(["tn_alpha", "tn_beta", "tn_gamma"], Sorted(client.PublishEntityCalls), "tn_beta failing should not stop tn_alpha and tn_gamma being published, once each.");
    }

    private static GlobalChoiceDefinition Choice(string name) => new()
    {
        Name = name,
        Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }],
    };
}

using D365Architect.Commands;
using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Commands.Solution;

/// <summary>
/// "<c>solution import</c> handles new views/forms the same way, after
/// the table and column stages, with the same plan/confirmation." The
/// orchestrator already runs views and forms after tables
/// (<c>SolutionImportOrchestratorTests</c>); what these pin is that
/// <see cref="ImportSolutionCommand.StepRunner"/> runs a view or form stage
/// through the create path, forwarding <c>solution import</c>'s own
/// <c>--solution</c> and <c>--yes</c> (today it passes neither a solution to the
/// view/form commands, which is the gap), and that a whole run over a new table's
/// folder ends with table, view and form all created, in that order.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportSolutionCommandViewFormCreateTests : IDisposable
{
    private const string Solution = "TNCore";
    private static readonly EntityIdentity ProjectIdentity = new(10722, "tn_projectid", "tn_name");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportSolutionCommandViewFormCreateTests() => Directory.CreateDirectory(Path.Combine(_root, "tn_project"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string TablePath => Path.Combine(_root, "tn_project", "tn_project.table.yml");
    private string ViewPath => Path.Combine(_root, "tn_project", "All projects.view.yml");
    private string FormPath => Path.Combine(_root, "tn_project", "Project.form.yml");

    private void WriteView() => File.WriteAllText(ViewPath, ViewYamlSerializer.ToYaml(ViewOf(
        name: "All projects",
        entity: "tn_project",
        fetchXml: "<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"tn_project\"><attribute name=\"tn_projectid\" /><attribute name=\"tn_name\" /></entity></fetch>",
        layoutXml: "<grid name=\"resultset\" object=\"10722\" jump=\"tn_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"tn_projectid\"><cell name=\"tn_name\" width=\"300\" /></row></grid>")));

    private void WriteForm() => File.WriteAllText(FormPath, FormYamlSerializer.ToYaml(FormOf("Project", entity: "tn_project")));

    private static FakeDataverseClient ClientWithProject()
    {
        var client = new FakeDataverseClient();
        client.EntityIdentities["tn_project"] = ProjectIdentity;
        return client;
    }

    private static ImportSolutionCommand CreateCommand(FakeDataverseClient client) =>
        new(
            new TableCreateService(client),
            new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)),
            new ViewImportService(client),
            new FormImportService(client, new FormJsonDefinitionReader()),
            new GlobalChoiceImportService(client),
            new ImportRunner(new FakeAuthenticationService()));

    private static ImportSolutionCommand.StepRunner CreateRunner(FakeDataverseClient client, bool noPublish = false) =>
        new(CreateCommand(client), new ImportSolutionCommand.Settings { Input = "unused", Solution = Solution, Yes = true, NoPublish = noPublish });

    // ---- the view stage creates, in the solution, without prompting ----

    // "Stories' folders with hand-written views ... import in one run" - the view stage creates a view that matches nothing.
    // Code path: ImportSolutionCommand.RunImportStepAsync(ImportView) -> ImportViewCommand.RunAsync with Solution forwarded.
    [Fact]
    public async Task ImportViewStage_NewView_CreatesItInTheSolutionWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: --yes must be forwarded
        WriteView();
        var client = ClientWithProject();

        var outcome = await CreateRunner(client).RunAsync(SolutionImportStage.ImportView, ViewPath, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, outcome, $"the view stage should succeed for a new view. Output: <{console.Output}>");
        var call = Assert.Single(client.CreateSavedQueryCalls);
        Expect.Equal(Solution, call.SolutionUniqueName, "solution import's --solution should reach the view create.");
        Expect.Equal("All projects", (string?)call.Body["name"], "the view in the folder should be the one created.");
    }

    // Change half: an existing view that changes joins the solution too.
    [Fact]
    public async Task ImportViewStage_ExistingView_IsUpdatedInTheSolution()
    {
        using var console = new ConsoleHarness();
        WriteView();
        var client = ClientWithProject();
        client.SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), null, "<fetch>old</fetch>", null);

        var outcome = await CreateRunner(client).RunAsync(SolutionImportStage.ImportView, ViewPath, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, outcome, $"The view stage should succeed for an existing view. Output: <{console.Output}>");
        Expect.Equal(Solution, Assert.Single(client.UpdateSavedQueryCalls).SolutionUniqueName, "solution import's --solution should reach the view update.");
    }

    // ---- the form stage creates, in the solution, and publishes ----

    [Fact]
    public async Task ImportFormStage_NewForm_CreatesItInTheSolutionAndPublishes()
    {
        using var console = new ConsoleHarness();
        WriteForm();
        var client = ClientWithProject();

        var outcome = await CreateRunner(client).RunAsync(SolutionImportStage.ImportForm, FormPath, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, outcome, $"the form stage should succeed for a new form. Output: <{console.Output}>");
        Expect.Equal(Solution, Assert.Single(client.CreateSystemFormCalls).SolutionUniqueName, "solution import's --solution should reach the form create.");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "a created form is published.");
    }

    // ---- the whole run ----

    // Example: one run over a new table's folder ends with the table, its view and its form created, in that order.
    // The runner below does the three stages that matter here for real and skips the column/choice stages (they have their own tests).
    // Dataverse only knows the table's type code once the table exists, so the identity appears after the create stage - the view stage
    // would find no table (and be refused) if it ran first.
    [Fact]
    public async Task WholeRun_NewTableWithHandWrittenViewAndForm_CreatesTableThenViewThenForm()
    {
        using var console = new ConsoleHarness();
        File.WriteAllText(TablePath, TableCreateFixtures.TableYaml());
        WriteView();
        WriteForm();
        var client = new FakeDataverseClient();
        client.EntityExists = _ => client.CreateEntityCalls.Count > 0;
        var runner = new TableThenViewAndFormRunner(CreateRunner(client), client);

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Equal(0, result.Failed, $"nothing in the run should fail. Output: <{console.Output}>");
        Expect.Sequence(["create-table:tn_Project", "create-view:All projects", "create-form:Project"], client.WriteOrder, $"the table should be created first, then its view, then its form. Output: <{console.Output}>");
        Expect.Equal(Solution, Assert.Single(client.CreateSavedQueryCalls).SolutionUniqueName, "the new view should join the solution.");
        Expect.Equal(Solution, Assert.Single(client.CreateSystemFormCalls).SolutionUniqueName, "the new form should join the solution.");
    }

    // "`solution import --no-publish` passes the option on to every step (nothing published)." The same whole run with the option set
    // still creates the table, view and form - in the same order - but publishes nothing, not even the form that always published before.
    // Code path: ImportSolutionCommand.Settings.NoPublish forwarded to every per-asset command's settings (CreateRunner reads it from there).
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task WholeRun_WithNoPublish_CreatesEverythingAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        File.WriteAllText(TablePath, TableCreateFixtures.TableYaml());
        WriteView();
        WriteForm();
        var client = new FakeDataverseClient();
        client.EntityExists = _ => client.CreateEntityCalls.Count > 0;
        var runner = new TableThenViewAndFormRunner(CreateRunner(client, noPublish: true), client);

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Equal(0, result.Failed, $"nothing in the run should fail. Output: <{console.Output}>");
        Expect.Sequence(["create-table:tn_Project", "create-view:All projects", "create-form:Project"], client.WriteOrder, $"--no-publish must not stop the table, view and form being created. Output: <{console.Output}>");
        PublishAssertions.NothingPublished(client, "'solution import --no-publish' should publish nothing at any step.");
    }

    private sealed class TableThenViewAndFormRunner(ImportSolutionCommand.StepRunner inner, FakeDataverseClient client) : ISolutionImportStepRunner
    {
        public async Task<SolutionTableCreateResult> CreateTablesAsync(IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken)
        {
            var result = await inner.CreateTablesAsync(tableAssetPaths, cancellationToken);
            client.EntityIdentities["tn_project"] = ProjectIdentity; // Dataverse assigns the type code at creation
            return result;
        }

        public async Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            if (stage is SolutionImportStage.ImportChoices or SolutionImportStage.ImportTable)
            {
                return SolutionImportStepOutcome.Succeeded;
            }

            var outcome = await inner.RunAsync(stage, assetPath, cancellationToken);
            if (stage == SolutionImportStage.CreateTable)
            {
                client.EntityIdentities["tn_project"] = ProjectIdentity; // (the per-table create step, until the batch replaces it)
            }

            return outcome;
        }

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName) => inner.ReportSkipped(stage, assetPath, tableLogicalName);
    }
}

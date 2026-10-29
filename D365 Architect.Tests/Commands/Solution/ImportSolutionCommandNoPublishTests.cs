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
/// "`solution import --no-publish` passes the option on to every step (nothing published)."
/// For each stage that publishes, <see cref="ImportSolutionCommand.StepRunner"/> runs that stage's own
/// command with <c>solution import</c>'s <see cref="ImportSolutionCommand.Settings.NoPublish"/> carried
/// over. Each test runs the stage twice over (Theory): with the option it still writes but publishes
/// nothing; without it the same write is followed by its publish (publish by default,
/// carried through <c>solution import</c>), so a stage that ignores the option can't pass by never publishing anyway.
/// The whole-run version is in <c>ImportSolutionCommandViewFormCreateTests</c>.
///
/// Tagged with the "Feature" trait "publish-by-default".
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportSolutionCommandNoPublishTests : IDisposable
{
    private static readonly EntityIdentity ProjectIdentity = new(10722, "tn_projectid", "tn_name");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportSolutionCommandNoPublishTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ImportSolutionCommand.StepRunner CreateRunner(FakeDataverseClient client, bool noPublish) =>
        new(
            new ImportSolutionCommand(
                new TableCreateService(client),
                new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)),
                new ViewImportService(client),
                new FormImportService(client, new FormJsonDefinitionReader()),
                new GlobalChoiceImportService(client),
                new ImportRunner(new FakeAuthenticationService())),
            new ImportSolutionCommand.Settings { Input = "unused", Solution = "TNCore", Yes = true, NoPublish = noPublish });

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    // Code path: StepRunner.RunImportStepAsync(ImportView) -> ImportViewCommand.Settings with NoPublish forwarded.
    [Theory]
    [Trait("Feature", "publish-by-default")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportViewStage_PublishesTheViewsTableUnlessNoPublish(bool noPublish)
    {
        using var console = new ConsoleHarness();
        var path = Write("All projects.view.yml", ViewYamlSerializer.ToYaml(ViewOf(
            name: "All projects",
            entity: "tn_project",
            fetchXml: "<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"tn_project\"><attribute name=\"tn_projectid\" /><attribute name=\"tn_name\" /></entity></fetch>",
            layoutXml: "<grid name=\"resultset\" object=\"10722\" jump=\"tn_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"tn_projectid\"><cell name=\"tn_name\" width=\"300\" /></row></grid>")));
        var client = new FakeDataverseClient();
        client.EntityIdentities["tn_project"] = ProjectIdentity;

        var outcome = await CreateRunner(client, noPublish).RunAsync(SolutionImportStage.ImportView, path, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, outcome, $"the view stage should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSavedQueryCalls.Count, "the view should still be created whether or not publishing is switched off.");
        if (noPublish)
        {
            PublishAssertions.NothingPublished(client, "the view step should honour solution import's --no-publish.");
        }
        else
        {
            Expect.Sequence(["tn_project"], client.PublishEntityCalls, "without --no-publish the view step should publish the view's table.");
        }
    }

    // Code path: StepRunner.RunImportStepAsync(ImportForm) -> ImportFormCommand.Settings with NoPublish forwarded (form import always published before).
    [Theory]
    [Trait("Feature", "publish-by-default")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportFormStage_PublishesTheFormsTableUnlessNoPublish(bool noPublish)
    {
        using var console = new ConsoleHarness();
        var path = Write("Project.form.yml", FormYamlSerializer.ToYaml(FormOf("Project", entity: "tn_project")));
        var client = new FakeDataverseClient();
        client.EntityIdentities["tn_project"] = ProjectIdentity;

        var outcome = await CreateRunner(client, noPublish).RunAsync(SolutionImportStage.ImportForm, path, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, outcome, $"the form stage should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSystemFormCalls.Count, "the form should still be created whether or not publishing is switched off.");
        if (noPublish)
        {
            PublishAssertions.NothingPublished(client, "the form step should honour solution import's --no-publish.");
        }
        else
        {
            Expect.Sequence(["tn_project"], client.PublishEntityCalls, "without --no-publish a created form's table is published.");
        }
    }

    // Code path: StepRunner.RunImportStepAsync(ImportTable) -> ImportTableCommand.Settings with NoPublish forwarded.
    [Theory]
    [Trait("Feature", "publish-by-default")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportTableStage_PublishesTheTableUnlessNoPublish(bool noPublish)
    {
        using var console = new ConsoleHarness();
        var column = AttributeDefinitionBuilder.Create("String", AutoNumberSchemaName, b =>
        {
            b.Name = AutoNumberLogicalName;
            b.AutoNumberFormat = "MSUM{SEQNUM:6}";
        });
        var path = Write("tn_test.table.yml", EntityYamlSerializer.ToYaml(new EntityDefinition { LogicalName = "tn_test", Attributes = [column] }));
        var client = new FakeDataverseClient { EntityDefinitionJson = Entity(), EntityMetadataJson = "{}" }; // live table lacks the column: the import adds it

        var outcome = await CreateRunner(client, noPublish).RunAsync(SolutionImportStage.ImportTable, path, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, outcome, $"the table stage should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateAttributeCalls.Count, "the new column should still be created whether or not publishing is switched off.");
        if (noPublish)
        {
            PublishAssertions.NothingPublished(client, "the table import step should honour solution import's --no-publish.");
        }
        else
        {
            Expect.Sequence(["tn_test"], client.PublishEntityCalls, "without --no-publish the table import step should publish the table it changed.");
        }
    }

    // Code path: StepRunner.RunImportStepAsync(ImportChoices) -> ImportChoiceCommand.Settings with NoPublish forwarded.
    [Theory]
    [Trait("Feature", "publish-by-default")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportChoicesStage_PublishesTheChoiceUnlessNoPublish(bool noPublish)
    {
        using var console = new ConsoleHarness();
        var choice = new GlobalChoiceDefinition { Name = "bogdan_status", Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }] };
        var path = Write("choices.yml", GlobalChoiceYamlSerializer.ToYaml([choice]));
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => null }; // not live: the import creates it

        var outcome = await CreateRunner(client, noPublish).RunAsync(SolutionImportStage.ImportChoices, path, CancellationToken.None);

        Expect.Equal(SolutionImportStepOutcome.Succeeded, outcome, $"the choices stage should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateGlobalOptionSetCalls.Count, "the choice should still be created whether or not publishing is switched off.");
        if (noPublish)
        {
            PublishAssertions.NothingPublished(client, "the choice import step should honour solution import's --no-publish.");
        }
        else
        {
            Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "without --no-publish the choice import step should publish the choice it created, in one call.");
            Expect.Sequence(["bogdan_status"], client.PublishGlobalOptionSetsCalls.FirstOrDefault() ?? [], "the created choice should be the one published.");
        }
    }
}

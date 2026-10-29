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

    private static ImportSolutionCommand.StepRunner CreateRunner(FakeDataverseClient client) =>
        new(
            new ImportSolutionCommand(
                new TableCreateService(client),
                new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)),
                new ViewImportService(client),
                new FormImportService(client, new FormJsonDefinitionReader()),
                new GlobalChoiceImportService(client),
                new ImportRunner(new FakeAuthenticationService())),
            new ImportSolutionCommand.Settings { Input = "unused", Solution = "TNCore", Yes = true });

    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task CreateTableStage_RunsTableCreateWithTheSolutionAndWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: --yes must be forwarded
        var client = new FakeDataverseClient { EntityExists = _ => false };

        var succeeded = await CreateRunner(client).RunAsync(SolutionImportStage.CreateTable, TablePath, CancellationToken.None);

        Expect.True(succeeded, $"The create stage should succeed for a missing table. Output: <{console.Output}>");
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
}

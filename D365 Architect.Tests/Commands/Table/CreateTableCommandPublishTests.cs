using D365Architect.Commands;
using D365Architect.Commands.Table;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.Table;

/// <summary>
/// <c>table create</c> publishes what it creates (REQ-1, 3, 4, 6 of the "publish by default" requirements,
/// tagged with the "Feature" trait "publish-by-default" because those IDs overlap older ones):
/// <see cref="CreateTableCommand"/> through the real <see cref="ImportRunner"/> and
/// <see cref="TableCreateService"/>, with only Dataverse (<see cref="FakeDataverseClient"/>), sign-in and
/// the console faked. The old <c>--publish</c> publish-only mode these tests used to cover is gone.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered
/// fails the test, which is how a single confirmation (no second prompt for the publish) is pinned.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class CreateTableCommandPublishTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public CreateTableCommandPublishTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteTableFile()
    {
        var path = Path.Combine(_directory, "tn_project.table.yml");
        File.WriteAllText(path, TableCreateFixtures.TableYaml());
        return path;
    }

    private static (CreateTableCommand Command, FakeDataverseClient Client) CreateCommand(bool tableExistsLive)
    {
        var client = new FakeDataverseClient { EntityExists = _ => tableExistsLive };
        return (new CreateTableCommand(new TableCreateService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static CreateTableCommand.Settings Settings(string input, bool yes = false, bool whatIf = false, bool noPublish = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, NoPublish = noPublish };

    // REQ-1: "`table create` publishes the table it just created. Example: creating reddt_invoicecentre ends with "Published.""
    // Code path: CreateTableCommand.RunAsync -> ImportRunner flow: after TableCreateService.ApplyAsync, ITableCreateService.PublishAsync -> IDataverseClient.PublishEntityAsync; prints "Published." last.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_CreatingATable_PublishesItAndEndsWithPublished()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-1: creating a table should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateEntityCalls.Count, "REQ-1: the table should be created.");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "REQ-1: 'table create' should publish the table it just created, once.");
        Expect.True(console.Output.TrimEnd().EndsWith("Published.", StringComparison.Ordinal), $"REQ-1: the run should end with \"Published.\". Output: <{console.Output}>");
    }

    // REQ-1 + the confirmation: answering yes to "create?" is the only question - publishing follows without asking again (the harness has one answer).
    [Fact]
    [Trait("Requirement", "REQ-1")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ConfirmingTheCreate_PublishesWithoutAskingAgain()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-1: confirming once should create and publish. Output: <{console.Output}>");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "REQ-1: the table should be published after the single confirmation.");
    }

    // REQ-3: "If nothing changed, nothing is published." - the table already exists, so there is nothing to create and nothing to publish.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_TableAlreadyExists_PublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-3: an existing table is not an error. Output: <{console.Output}>");
        PublishAssertions.NothingPublished(client, "REQ-3", "nothing was created, so nothing should be published.");
    }

    // REQ-3 (and the old prompt): answering no creates nothing, so nothing is published.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_DecliningTheCreate_PublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"Declining the confirmation should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "Declining should create nothing.");
        PublishAssertions.NothingPublished(client, "REQ-3", "declining wrote nothing, so there is nothing to publish.");
    }

    // REQ-4: "`--no-publish` skips publishing" - the table is still created.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithNoPublish_CreatesTheTableButPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true, noPublish: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-4: --no-publish should still create the table. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateEntityCalls.Count, "REQ-4: --no-publish should still create the table.");
        PublishAssertions.NothingPublished(client, "REQ-4", "--no-publish skips publishing.");
    }

    // REQ-6: "`--whatif` never publishes." - it never creates either, and never prompts.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithWhatIf_CreatesAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-6: --whatif should succeed. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "REQ-6: --whatif must not create the table.");
        PublishAssertions.NothingPublished(client, "REQ-6", "--whatif never publishes.");
    }
}

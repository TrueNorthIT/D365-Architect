using D365Architect.Commands;
using D365Architect.Commands.Table;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.Table;

/// <summary>
/// <c>table create</c> publishes what it creates (the "publish by default" behaviour,
/// tagged with the "Feature" trait "publish-by-default"):
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

    // "`table create` publishes the table it just created. Example: creating reddt_invoicecentre ends with "Published.""
    // Code path: CreateTableCommand.RunAsync -> ImportRunner flow: after TableCreateService.ApplyAsync, ITableCreateService.PublishAsync -> IDataverseClient.PublishEntityAsync; prints "Published." last.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_CreatingATable_PublishesItAndEndsWithPublished()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"creating a table should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateEntityCalls.Count, "the table should be created.");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "'table create' should publish the table it just created, once.");
        Expect.True(console.Output.TrimEnd().EndsWith("Published.", StringComparison.Ordinal), $"the run should end with \"Published.\". Output: <{console.Output}>");
    }

    // Answering yes to "create?" is the only question - publishing follows without asking again (the harness has one answer).
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ConfirmingTheCreate_PublishesWithoutAskingAgain()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"confirming once should create and publish. Output: <{console.Output}>");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "the table should be published after the single confirmation.");
    }

    // "If nothing changed, nothing is published." - the table already exists, so there is nothing to create and nothing to publish.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_TableAlreadyExists_PublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"an existing table is not an error. Output: <{console.Output}>");
        PublishAssertions.NothingPublished(client, "nothing was created, so nothing should be published.");
    }

    // Answering no at the create prompt creates nothing, so nothing is published.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_DecliningTheCreate_PublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"Declining the confirmation should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "Declining should create nothing.");
        PublishAssertions.NothingPublished(client, "declining wrote nothing, so there is nothing to publish.");
    }

    // "`--no-publish` skips publishing" - the table is still created.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithNoPublish_CreatesTheTableButPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true, noPublish: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"--no-publish should still create the table. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateEntityCalls.Count, "--no-publish should still create the table.");
        PublishAssertions.NothingPublished(client, "--no-publish skips publishing.");
    }

    // "`--whatif` never publishes." - it never creates either, and never prompts.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithWhatIf_CreatesAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"--whatif should succeed. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "--whatif must not create the table.");
        PublishAssertions.NothingPublished(client, "--whatif never publishes.");
    }
}

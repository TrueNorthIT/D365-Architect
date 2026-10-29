using D365Architect.Commands;
using D365Architect.Commands.Table;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands.Table;

/// <summary>
/// <c>table create --publish</c> as a user meets it (REQ-34, 35, 37, 39):
/// <see cref="CreateTableCommand"/> through the real <see cref="ImportRunner"/>
/// and <see cref="TableCreateService"/>, with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked.
///
/// Decision pinned here: for <c>table create --publish</c> "what the file
/// describes exists" means the TABLE exists (<c>EntityExistsAsync</c>). Its
/// other columns are <c>table import</c>'s job, so a file with columns that
/// aren't live yet still publishes - there is no REQ-36 refusal for
/// <c>table create</c>.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a
/// prompt nobody answered fails the test, which is how <c>--yes</c> is shown
/// never to ask.
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

    private static CreateTableCommand.Settings Settings(string input, bool yes = false, bool whatIf = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, Publish = true };

    // REQ-34: "`table create` ... accept `--publish`" - it is a real command-line option.
    // Code path: CreateTableCommand.Settings (via PublishableImportSettings.Publish) needs a [CommandOption("--publish")].
    [Fact]
    [Trait("Requirement", "REQ-34")]
    public async Task TableCreate_OffersAPublishOption()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            TableCommands.Configure(config);
        });

        var exitCode = await app.RunAsync(["table", "create", "--help"]);

        Expect.Equal(0, exitCode, $"REQ-34: 'table create --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--publish", console.Output, "REQ-34: 'table create' should offer --publish.");
    }

    // REQ-34 example: `table create -i invoicecentre.table.yml --publish` -> publishes the table; REQ-37: a table is published whole (its views and forms with it).
    // Code path: CreateTableCommand.RunAsync with settings.Publish publishing via IDataverseClient.PublishEntityAsync, the call form import already uses.
    [Fact]
    [Trait("Requirement", "REQ-34")]
    public async Task RunAsync_PublishOfAnExistingTable_PublishesThatTableAndWritesNothingElse()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-34: publishing an existing table should succeed. Output: <{console.Output}>");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "REQ-34/REQ-37: 'table create --publish' should publish the table from the file, once, as a whole (which carries its views and forms).");
        Expect.Empty(client.PublishGlobalOptionSetsCalls, "REQ-34: a table publish should not publish any global choices.");
        PublishAssertions.NothingWritten(client, "REQ-34");
    }

    // Pinned decision (REQ-34/35): the table existing is enough. The file lists columns tn_budget and tn_startdate that
    // aren't live; creating them is the job of 'table import', so 'table create --publish' still publishes.
    [Fact]
    [Trait("Requirement", "REQ-34")]
    public async Task RunAsync_PublishWhenTheFileHasColumnsNotLive_StillPublishesBecauseOnlyTheTableMustExist()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true); // no column lookups are configured: none may be needed

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-34: an existing table should publish even though the file's other columns aren't live. Output: <{console.Output}>");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "REQ-34: the table should be published; its other columns are the job of 'table import'.");
    }

    // REQ-35 example: Table missing -> "tn_project doesn't exist yet - run `table create` without --publish first".
    // Code path: CreateTableCommand.RunAsync in publish mode, using TableCreatePreview.AlreadyExists == false as "doesn't exist".
    [Fact]
    [Trait("Requirement", "REQ-35")]
    public async Task RunAsync_PublishOfAMissingTable_SaysToRunTableCreateWithoutPublishFirstAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-35: publishing a table that doesn't exist should exit with 1. Output: <{console.Output}>");
        Expect.Contains("tn_project doesn't exist yet", console.Output, "REQ-35: the user should be told which table doesn't exist yet.");
        Expect.Contains("table create", console.Output, "REQ-35: the user should be told to run 'table create'.");
        Expect.Contains("without --publish first", console.Output, "REQ-35: the user should be told to run it without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-35", "a missing table must not be published.");
        PublishAssertions.NothingWritten(client, "REQ-35");
    }

    // REQ-39 example: `--publish --whatif` prints "would publish: tn_project"; nothing is published and nothing is prompted.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithWhatIf_PrintsWouldPublishTheTableAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: --publish --whatif should succeed. Output: <{console.Output}>");
        Expect.Contains("would publish: tn_project", console.Output, "REQ-39: --whatif should say what it would publish.");
        PublishAssertions.NothingPublished(client, "REQ-39", "--whatif writes nothing, including no publish.");
    }

    // REQ-39: "asks first" - answering no publishes nothing and exits 0, as every import does. Proves the prompt exists: without one this would publish.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_DecliningThePromptPublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: declining the prompt should exit 0. Output: <{console.Output}>");
        Expect.Contains("would publish: tn_project", console.Output, "REQ-39: the run should show what it is about to publish before asking.");
        PublishAssertions.NothingPublished(client, "REQ-39", "answering no must not publish.");
    }

    // REQ-39: answering yes publishes.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_AcceptingThePromptPublishesTheTable()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: confirming should publish and exit 0. Output: <{console.Output}>");
        Expect.Sequence(["tn_project"], client.PublishEntityCalls, "REQ-39: answering yes should publish the table once.");
    }
}

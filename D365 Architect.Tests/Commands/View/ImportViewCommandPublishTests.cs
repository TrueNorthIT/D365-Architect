using D365Architect.Commands;
using D365Architect.Commands.View;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Commands.View;

/// <summary>
/// <c>view import --publish</c> as a user meets it (REQ-34, 35, 36, 37, 39):
/// <see cref="ImportViewCommand"/> through the real <see cref="ImportRunner"/>
/// and <see cref="ViewImportService"/>, with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked.
///
/// Decisions pinned here, for the developer:
/// <list type="bullet">
/// <item>Dataverse publishes a view as part of its table, so publishing a view publishes
/// the view's table (<c>ViewDefinition.Entity</c>) through <c>PublishEntityAsync</c>.</item>
/// <item>"Doesn't exist yet" is a view the import would CREATE (<see cref="ViewImportPreview.IsCreate"/>),
/// or a <c>savedQueryId</c> that matches no live view (today a <see cref="ViewNotFoundException"/>); either
/// way the answer is "run view import without --publish first", never the raw exception text.</item>
/// <item>"Unapplied changes" are what <see cref="ViewImportPreview.HasChanges"/> says is still to
/// write. These tests only pin the count for a single differing field ("1 unapplied change").</item>
/// </list>
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a
/// prompt nobody answered fails the test, which is how <c>--yes</c> is shown
/// never to ask.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportViewCommandPublishTests : IDisposable
{
    private const string ViewName = "All Invoice Centres";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportViewCommandPublishTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteView(ViewDefinition view)
    {
        var path = Path.Combine(_directory, "all.view.yml");
        File.WriteAllText(path, ViewYamlSerializer.ToYaml(view));
        return path;
    }

    /// <summary>A view file that only carries a FetchXML, so the live view either matches it exactly or differs in that one field.</summary>
    private static ViewDefinition FetchOnlyView(Guid? savedQueryId = null) => ViewOf(name: ViewName, fetchXml: ExportedFetchXml, savedQueryId: savedQueryId);

    private static ExistingSavedQuery LiveView(string fetchXml, Guid? id = null) => new(id ?? Guid.NewGuid(), null, fetchXml, ExportedLayoutXml());

    private static (ImportViewCommand Command, FakeDataverseClient Client) CreateCommand(FakeDataverseClient? client = null)
    {
        client ??= ClientWithTable();
        return (new ImportViewCommand(new ViewImportService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportViewCommand.Settings Settings(string input, bool yes = false, bool whatIf = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, Publish = true };

    // REQ-34: "`view import` ... accept `--publish`" - it is a real command-line option.
    // Code path: ImportViewCommand.Settings (via PublishableImportSettings.Publish) needs a [CommandOption("--publish")].
    [Fact]
    [Trait("Requirement", "REQ-34")]
    public async Task ViewImport_OffersAPublishOption()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            ViewCommands.Configure(config);
        });

        var exitCode = await app.RunAsync(["view", "import", "--help"]);

        Expect.Equal(0, exitCode, $"REQ-34: 'view import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--publish", console.Output, "REQ-34: 'view import' should offer --publish.");
    }

    // REQ-37 example: `view import -i all.view.yml --publish` -> publishes the view's table, since Dataverse publishes views as part of their table. REQ-34: nothing else is written.
    // Code path: ImportViewCommand.RunAsync in publish mode -> IDataverseClient.PublishEntityAsync(view.Entity).
    [Fact]
    [Trait("Requirement", "REQ-37")]
    public async Task RunAsync_PublishOfAnUpToDateView_PublishesItsTableAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var client = ClientWithTable();
        client.SavedQuery = LiveView(ExportedFetchXml);
        var (command, _) = CreateCommand(client);

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView()), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-37: publishing an up-to-date view should succeed. Output: <{console.Output}>");
        Expect.Sequence([TableName], client.PublishEntityCalls, "REQ-37: 'view import --publish' should publish the view's table, once.");
        Expect.Empty(client.PublishGlobalOptionSetsCalls, "REQ-37: a view publish should not publish any global choices.");
        PublishAssertions.NothingWritten(client, "REQ-34");
    }

    // REQ-37: the same when the view is identified by its savedQueryId rather than its name.
    [Fact]
    [Trait("Requirement", "REQ-37")]
    public async Task RunAsync_PublishOfAnUpToDateViewFoundById_PublishesItsTable()
    {
        using var console = new ConsoleHarness();
        var id = Guid.NewGuid();
        var client = ClientWithTable();
        client.SavedQueriesById[id] = new ExistingSavedQuery(id, null, ExportedFetchXml, ExportedLayoutXml(), TableName, ViewName);
        var (command, _) = CreateCommand(client);

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView(id)), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-37: publishing a view found by savedQueryId should succeed. Output: <{console.Output}>");
        Expect.Sequence([TableName], client.PublishEntityCalls, "REQ-37: the view's table should be published.");
    }

    // REQ-35: "view not found (by id or name) means 'run view import without --publish first'" - by name, it would be a create.
    // Code path: ImportViewCommand.RunAsync in publish mode, treating ViewImportPreview.IsCreate as "doesn't exist yet".
    [Fact]
    [Trait("Requirement", "REQ-35")]
    public async Task RunAsync_PublishOfAViewThatDoesNotExistByName_SaysToRunViewImportWithoutPublishFirst()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(); // no live view: a plain import would create it

        var exitCode = await command.RunAsync(Settings(WriteView(ViewOf(name: ViewName, fetchXml: ExportedFetchXml, layoutXml: ExportedLayoutXml())), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-35: publishing a view that doesn't exist should exit with 1. Output: <{console.Output}>");
        Expect.Contains(ViewName, console.Output, "REQ-35: the user should be told which view doesn't exist yet.");
        Expect.Contains("doesn't exist yet", console.Output, "REQ-35: the user should be told the view doesn't exist yet.");
        Expect.Contains("view import", console.Output, "REQ-35: the user should be told to run 'view import'.");
        Expect.Contains("without --publish first", console.Output, "REQ-35: the user should be told to run it without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-35", "a missing view must not cause a publish.");
        PublishAssertions.NothingWritten(client, "REQ-35");
    }

    // REQ-35: and by id - a savedQueryId matching no live view gets the same advice, not the raw "no view with that id" error.
    // Code path: ImportViewCommand.RunAsync in publish mode, translating ViewNotFoundException from ViewImportService.PreviewAsync.
    [Fact]
    [Trait("Requirement", "REQ-35")]
    public async Task RunAsync_PublishOfAViewWhoseIdMatchesNothing_SaysToRunViewImportWithoutPublishFirst()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView(Guid.NewGuid())), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-35: publishing a view whose id isn't live should exit with 1. Output: <{console.Output}>");
        Expect.Contains("doesn't exist yet", console.Output, "REQ-35: the user should be told the view doesn't exist yet.");
        Expect.Contains("without --publish first", console.Output, "REQ-35: the user should be told to run view import without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-35", "a view that isn't live must not cause a publish.");
    }

    // REQ-36: the file's FetchXML differs from the live view, so a plain import would write it - refuse rather than publish something other than the file.
    // Code path: ImportViewCommand.RunAsync in publish mode, using ViewImportPreview.HasChanges.
    [Fact]
    [Trait("Requirement", "REQ-36")]
    public async Task RunAsync_PublishWhenTheFileDiffersFromTheLiveView_RefusesAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithTable();
        client.SavedQuery = LiveView("<fetch>old</fetch>");
        var (command, _) = CreateCommand(client);

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView()), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-36: a file with an unapplied change should be refused with exit code 1. Output: <{console.Output}>");
        Expect.Contains("1 unapplied change", console.Output, "REQ-36: the refusal should say how many unapplied changes the file has.");
        Expect.Contains("without --publish first", console.Output, "REQ-36: the refusal should say to run the command without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-36", "publishing must not go ahead when the file differs from what is live.");
        PublishAssertions.NothingWritten(client, "REQ-36");
    }

    // REQ-39 example: `--publish --whatif` prints "would publish: <table>"; nothing is published and nothing is prompted.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithWhatIf_PrintsWouldPublishTheTableAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var client = ClientWithTable();
        client.SavedQuery = LiveView(ExportedFetchXml);
        var (command, _) = CreateCommand(client);

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView()), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: --publish --whatif should succeed. Output: <{console.Output}>");
        Expect.Contains($"would publish: {TableName}", console.Output, "REQ-39: --whatif should say which table it would publish.");
        PublishAssertions.NothingPublished(client, "REQ-39", "--whatif writes nothing, including no publish.");
    }

    // REQ-39: "asks first" - answering no publishes nothing and exits 0. Proves the prompt exists: without one this would publish.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_DecliningThePromptPublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var client = ClientWithTable();
        client.SavedQuery = LiveView(ExportedFetchXml);
        var (command, _) = CreateCommand(client);

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: declining the prompt should exit 0. Output: <{console.Output}>");
        Expect.Contains($"would publish: {TableName}", console.Output, "REQ-39: the run should show what it is about to publish before asking.");
        PublishAssertions.NothingPublished(client, "REQ-39", "answering no must not publish.");
    }

    // REQ-39: answering yes publishes.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_AcceptingThePromptPublishesTheTable()
    {
        using var console = new ConsoleHarness("y");
        var client = ClientWithTable();
        client.SavedQuery = LiveView(ExportedFetchXml);
        var (command, _) = CreateCommand(client);

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: confirming should publish and exit 0. Output: <{console.Output}>");
        Expect.Sequence([TableName], client.PublishEntityCalls, "REQ-39: answering yes should publish the table once.");
    }
}

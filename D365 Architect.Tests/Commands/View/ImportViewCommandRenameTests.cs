using D365Architect.Commands;
using D365Architect.Commands.View;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.View;

/// <summary>
/// <c>view import</c> as a user meets it when the file renames a view (its <c>savedQueryId</c> resolves to a live view with a
/// different name): <see cref="ImportViewCommand"/> through the real <see cref="ImportRunner"/> and <see cref="ViewImportService"/>,
/// with only Dataverse (<see cref="FakeDataverseClient"/>), sign-in and the console faked. The service-level behaviour is in
/// <c>ViewImportServiceRenameTests</c>.
///
/// Output contract: the plan prints the rename as <c>name:</c> followed by <c>'&lt;old&gt;' -&gt; '&lt;new&gt;'</c>, and a
/// <see cref="ViewImportPreview.DuplicateNameWarning"/> is printed as <c>Warning: &lt;text&gt;</c> beside the other warnings.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered fails the test.
/// Tagged with the "Feature" trait "view-rename".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "view-rename")]
public sealed class ImportViewCommandRenameTests : IDisposable
{
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid OtherViewId = new("aaaaaaaa-0000-0000-0000-000000000002");

    private const string LiveName = "Active Accounts (LT)";
    private const string NewName = "Active Accounts Renamed";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportViewCommandRenameTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>A file that only renames: same id as the live view, a new <c>view:</c> value, nothing else to write.</summary>
    private string WriteRenameOnlyFile()
    {
        var path = Path.Combine(_directory, "account-active.view.yml");
        File.WriteAllText(path, ViewYamlSerializer.ToYaml(new ViewDefinition { Name = NewName, Entity = "account", SavedQueryId = ViewId }));
        return path;
    }

    private static (ImportViewCommand Command, FakeDataverseClient Client) CreateCommand()
    {
        var client = new FakeDataverseClient();
        client.SavedQueriesById[ViewId] = new ExistingSavedQuery(ViewId, null, "<fetch/>", null, "account", LiveName);
        return (new ImportViewCommand(new ViewImportService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportViewCommand.Settings Settings(string input, bool yes = false, bool whatIf = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf };

    // REQ-2: "The diff and --whatif show the rename (old and new name visible), and --whatif writes nothing."
    // Code path: ImportViewCommand's ImportFlowSpec.PrintChanges for a ViewImportPreview with ExistingName != Name.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public async Task RunAsync_RenameWithWhatIf_ShowsTheOldAndNewNameAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteRenameOnlyFile(), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-2: --whatif on a rename should succeed. Output: <{console.Output}>");
        Expect.Contains($"'{LiveName}' -> '{NewName}'", console.Output, "REQ-2: the plan should show the rename as 'old' -> 'new'.");
        Expect.Empty(client.UpdateSavedQueryCalls, "REQ-2: --whatif must not write the rename.");
    }

    // REQ-3: "A rename on its own counts as a change, so it's confirmed and published like any other."
    // One confirmation, then the name is written and the view's table is published.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public async Task RunAsync_RenameOnlyAndConfirmed_WritesTheNameAndPublishesTheTable()
    {
        using var console = new ConsoleHarness("y"); // one answer: a second prompt would fail the test
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteRenameOnlyFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-3: confirming a rename should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, $"REQ-3: the confirmed rename should be written in one update. Output: <{console.Output}>");
        Expect.Equal(NewName, client.UpdateSavedQueryCalls[0].Name, "REQ-3: the update should carry the new name.");
        Expect.Sequence(["account"], client.PublishEntityCalls, "REQ-3: a written rename should publish the view's table, once.");
    }

    // REQ-3: declining the rename writes and publishes nothing, like any other change.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public async Task RunAsync_RenameOnlyAndDeclined_WritesAndPublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteRenameOnlyFile()), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-3: declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Contains("Import these changes into Dataverse?", console.Output, "REQ-3: a rename should be put to the user for confirmation.");
        Expect.Empty(client.UpdateSavedQueryCalls, "REQ-3: declining should write nothing.");
        PublishAssertions.NothingPublished(client, "REQ-3: declining wrote nothing, so there is nothing to publish.");
    }

    // REQ-8 (and REQ-3 once): the same file twice. The first run renames and publishes; the second finds the view already named
    // as the file says, reports no changes, and writes and publishes nothing more.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task RunAsync_RunningTheSameFileTwice_RenamesOnceThenReportsNoChanges()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();
        var input = WriteRenameOnlyFile();

        await command.RunAsync(Settings(input, yes: true), CancellationToken.None);
        var outputAfterFirstRun = console.Output;
        var exitCode = await command.RunAsync(Settings(input, yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-8: the second import should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, $"REQ-8: only the first import should write; the second should find nothing to do. First run's output: <{outputAfterFirstRun}>");
        Expect.Contains("No changes", console.Output[outputAfterFirstRun.Length..], "REQ-8: the second import should say there are no changes.");
        Expect.Sequence(["account"], client.PublishEntityCalls, "REQ-8: only the first import should publish.");
    }

    // REQ-9: "goes ahead, with a warning" - the warning is printed and, with --yes, the rename is still written.
    // Code path: ImportViewCommand's ImportFlowSpec.OnPreviewBuilt prints ViewImportPreview.DuplicateNameWarning as "Warning: ...".
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task RunAsync_RenameToADuplicateName_WarnsAndStillRenames()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();
        client.NamedSavedQueries.Add((OtherViewId, "account", NewName));

        var exitCode = await command.RunAsync(Settings(WriteRenameOnlyFile(), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-9: a duplicate name should not fail the import. Output: <{console.Output}>");
        Expect.Contains("Warning:", console.Output, "REQ-9: the user should be warned that another view on the table already has the new name.");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, $"REQ-9: the rename should still be written. Output: <{console.Output}>");
        Expect.Equal(NewName, client.UpdateSavedQueryCalls[0].Name, "REQ-9: the update should carry the new name.");
    }
}

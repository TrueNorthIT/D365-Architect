using D365Architect.Commands;
using D365Architect.Commands.View;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Commands.View;

/// <summary>
/// <c>view import</c> publishes after it applies changes (the "publish by default"
/// behaviour, tagged with the "Feature" trait "publish-by-default"):
/// <see cref="ImportViewCommand"/> through the real <see cref="ImportRunner"/> and
/// <see cref="ViewImportService"/>, with only Dataverse (<see cref="FakeDataverseClient"/>), sign-in and the
/// console faked. The old <c>--publish</c> publish-only mode these tests used to cover is gone.
///
/// Dataverse publishes a view as part of its table, so "publish the view" means publishing the view's
/// table (<c>ViewDefinition.Entity</c>) through <c>PublishEntityAsync</c>.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered
/// fails the test, which is how a single confirmation (no second prompt for the publish) is pinned.
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
    private static ViewDefinition FetchOnlyView() => ViewOf(name: ViewName, fetchXml: ExportedFetchXml);

    private static ExistingSavedQuery LiveView(string fetchXml) => new(Guid.NewGuid(), null, fetchXml, ExportedLayoutXml());

    /// <summary>A live table and, when <paramref name="liveFetchXml"/> is given, a live view with that FetchXML; with none, the import would create the view.</summary>
    private static (ImportViewCommand Command, FakeDataverseClient Client) CreateCommand(string? liveFetchXml = null)
    {
        var client = ClientWithTable();
        if (liveFetchXml is not null)
        {
            client.SavedQuery = LiveView(liveFetchXml);
        }

        return (new ImportViewCommand(new ViewImportService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportViewCommand.Settings Settings(string input, bool yes = false, bool whatIf = false, bool noPublish = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, NoPublish = noPublish };

    // "`view import` ... publish after applying changes." - an existing view whose FetchXML changes is updated, then its table published.
    // Code path: ImportViewCommand.RunAsync -> ImportRunner flow: after ViewImportService.ApplyAsync, IViewImportService.PublishAsync -> IDataverseClient.PublishEntityAsync(view.Entity).
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_UpdatingAView_PublishesItsTable()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client) = CreateCommand(liveFetchXml: "<fetch>old</fetch>");

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView()), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"updating a view should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "the changed view should be updated.");
        Expect.Sequence([TableName], client.PublishEntityCalls, "'view import' should publish the view's table, once.");
        Expect.Empty(client.PublishGlobalOptionSetsCalls, "a view import should not publish any global choices.");
    }

    // A view that is created (no live view matches) is published too.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_CreatingAView_PublishesItsTable()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteView(ViewOf(name: ViewName, fetchXml: ExportedFetchXml, layoutXml: ExportedLayoutXml())), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"creating a view should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSavedQueryCalls.Count, "the new view should be created.");
        Expect.Sequence([TableName], client.PublishEntityCalls, "a created view's table should be published.");
    }

    // Answering yes to "Import these changes?" is the only question - publishing follows without asking again.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ConfirmingTheImport_PublishesWithoutAskingAgain()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand(liveFetchXml: "<fetch>old</fetch>");

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"confirming once should import and publish. Output: <{console.Output}>");
        Expect.Sequence([TableName], client.PublishEntityCalls, "the view's table should be published after the single confirmation.");
    }

    // "If nothing changed, nothing is published." - the live view already matches the file.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_FileMatchesLive_PublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(liveFetchXml: ExportedFetchXml);

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView()), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"an in-sync view is not an error. Output: <{console.Output}>");
        PublishAssertions.NothingPublished(client, "nothing changed, so nothing should be published.");
    }

    // Answering no applies nothing, so nothing is published.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_DecliningTheImport_PublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand(liveFetchXml: "<fetch>old</fetch>");

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"Declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.UpdateSavedQueryCalls, "Declining should write nothing.");
        PublishAssertions.NothingPublished(client, "declining wrote nothing, so there is nothing to publish.");
    }

    // "`--no-publish` skips publishing" - the view is still updated.
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithNoPublish_AppliesTheChangeButPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(liveFetchXml: "<fetch>old</fetch>");

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView()), yes: true, noPublish: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"--no-publish should still import. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "--no-publish should still write the change.");
        PublishAssertions.NothingPublished(client, "--no-publish skips publishing.");
    }

    // "`--whatif` never publishes."
    [Fact]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithWhatIf_WritesAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand(liveFetchXml: "<fetch>old</fetch>");

        var exitCode = await command.RunAsync(Settings(WriteView(FetchOnlyView()), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"--whatif should succeed. Output: <{console.Output}>");
        Expect.Empty(client.UpdateSavedQueryCalls, "--whatif must not write.");
        PublishAssertions.NothingPublished(client, "--whatif never publishes.");
    }
}

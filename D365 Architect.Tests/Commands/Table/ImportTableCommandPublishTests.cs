using D365Architect.Commands;
using D365Architect.Commands.Table;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.RichTextAndAutoNumberFixtures;

namespace D365Architect.Tests.Commands.Table;

/// <summary>
/// <c>table import</c> publishes after it applies changes (REQ-2, 3, 4, 6 of the "publish by default"
/// requirements, tagged with the "Feature" trait "publish-by-default" because those IDs overlap older ones):
/// <see cref="ImportTableCommand"/> through the real <see cref="ImportRunner"/> and
/// <see cref="TableImportService"/>, with only Dataverse (<see cref="FakeDataverseClient"/>), sign-in and the
/// console faked. The old <c>--publish</c> publish-only mode these tests used to cover is gone.
///
/// The live table <c>tn_test</c> is either empty (so a file with the auto-number column changes it) or already
/// has that column (so the same file changes nothing).
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered
/// fails the test, which is how a single confirmation (no second prompt for the publish) is pinned.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportTableCommandPublishTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportTableCommandPublishTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static AttributeDefinition AutoNumberColumn(int? seed = null) =>
        AttributeDefinitionBuilder.Create("String", AutoNumberSchemaName, b =>
        {
            b.Name = AutoNumberLogicalName;
            b.AutoNumberFormat = "MSUM{SEQNUM:6}";
            b.AutoNumberSeed = seed;
        });

    private string WriteTableFile(AttributeDefinition column)
    {
        var path = Path.Combine(_directory, "tn_test.table.yml");
        File.WriteAllText(path, EntityYamlSerializer.ToYaml(new EntityDefinition { LogicalName = "tn_test", Attributes = [column] }));
        return path;
    }

    private static (ImportTableCommand Command, FakeDataverseClient Client) CreateCommand(string liveEntityJson)
    {
        var client = new FakeDataverseClient { EntityExists = _ => true, EntityDefinitionJson = liveEntityJson, EntityMetadataJson = "{}" };
        var service = new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));
        return (new ImportTableCommand(service, new ImportRunner(new FakeAuthenticationService())), client);
    }

    /// <summary>The live table has no columns, so importing the file adds one.</summary>
    private (ImportTableCommand Command, FakeDataverseClient Client, string Input) WithOneChange(int? seed = null)
    {
        var (command, client) = CreateCommand(Entity());
        return (command, client, WriteTableFile(AutoNumberColumn(seed)));
    }

    /// <summary>The live table already has the file's column, so importing it changes nothing.</summary>
    private (ImportTableCommand Command, FakeDataverseClient Client, string Input) WithNoChange(int? seed = null)
    {
        var (command, client) = CreateCommand(Entity(LiveAutoNumberString));
        return (command, client, WriteTableFile(AutoNumberColumn(seed)));
    }

    private static ImportTableCommand.Settings Settings(string input, bool yes = false, bool whatIf = false, bool noPublish = false, bool noTransaction = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, NoPublish = noPublish, NoTransaction = noTransaction };

    // REQ-2: "`table import` ... publish after applying changes."
    // Code path: ImportTableCommand.RunAsync -> ImportRunner flow: after TableImportService.ApplyAsync, ITableImportService.PublishAsync -> IDataverseClient.PublishEntityAsync.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ApplyingAChange_PublishesTheTable()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client, input) = WithOneChange();

        var exitCode = await command.RunAsync(Settings(input, yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-2: importing a new column should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateAttributeCalls.Count, "REQ-2: the new column should be created.");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "REQ-2: 'table import' should publish the table it changed, once.");
        Expect.Empty(client.PublishGlobalOptionSetsCalls, "REQ-2: a table import should not publish any global choices.");
    }

    // REQ-2: the same when the column is written one request at a time (--no-transaction) rather than as a changeset.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ApplyingAChangeWithoutATransaction_PublishesTheTable()
    {
        using var console = new ConsoleHarness();
        var (command, client, input) = WithOneChange();

        var exitCode = await command.RunAsync(Settings(input, yes: true, noTransaction: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-2: importing with --no-transaction should succeed. Output: <{console.Output}>");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "REQ-2: --no-transaction changes how columns are written, not whether the table is published.");
    }

    // REQ-2 + the confirmation: answering yes to "Import these changes?" is the only question - publishing follows without asking again.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ConfirmingTheImport_PublishesWithoutAskingAgain()
    {
        using var console = new ConsoleHarness("y");
        var (command, client, input) = WithOneChange();

        var exitCode = await command.RunAsync(Settings(input), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-2: confirming once should import and publish. Output: <{console.Output}>");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "REQ-2: the table should be published after the single confirmation.");
    }

    // REQ-3: "If nothing changed, nothing is published." - the file matches what is live.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_FileMatchesLive_PublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client, input) = WithNoChange();

        var exitCode = await command.RunAsync(Settings(input, yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-3: an in-sync table is not an error. Output: <{console.Output}>");
        PublishAssertions.NothingPublished(client, "REQ-3", "nothing changed, so nothing should be published.");
    }

    // REQ-3: a seed on an existing column is only a warning ("seed ignored"), not a change - still nothing to publish.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_OnlyAnIgnoredSeedDiffers_PublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client, input) = WithNoChange(seed: 1);

        var exitCode = await command.RunAsync(Settings(input, yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-3: an ignored seed is not an error. Output: <{console.Output}>");
        PublishAssertions.NothingPublished(client, "REQ-3", "an ignored seed changes nothing, so nothing should be published.");
    }

    // REQ-3: answering no applies nothing, so nothing is published.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_DecliningTheImport_PublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client, input) = WithOneChange();

        var exitCode = await command.RunAsync(Settings(input), CancellationToken.None);

        Expect.Equal(0, exitCode, $"Declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.CreateAttributeCalls, "Declining should write nothing.");
        PublishAssertions.NothingPublished(client, "REQ-3", "declining wrote nothing, so there is nothing to publish.");
    }

    // REQ-4: "`--no-publish` skips publishing" - the column is still written.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithNoPublish_AppliesTheChangeButPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client, input) = WithOneChange();

        var exitCode = await command.RunAsync(Settings(input, yes: true, noPublish: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-4: --no-publish should still import. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateAttributeCalls.Count, "REQ-4: --no-publish should still write the change.");
        PublishAssertions.NothingPublished(client, "REQ-4", "--no-publish skips publishing.");
    }

    // REQ-6: "`--whatif` never publishes."
    [Fact]
    [Trait("Requirement", "REQ-6")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithWhatIf_WritesAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client, input) = WithOneChange();

        var exitCode = await command.RunAsync(Settings(input, whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-6: --whatif should succeed. Output: <{console.Output}>");
        Expect.Empty(client.CreateAttributeCalls, "REQ-6: --whatif must not write.");
        PublishAssertions.NothingPublished(client, "REQ-6", "--whatif never publishes.");
    }
}

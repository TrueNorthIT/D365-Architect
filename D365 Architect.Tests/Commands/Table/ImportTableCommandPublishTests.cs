using D365Architect.Commands;
using D365Architect.Commands.Table;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;
using static D365Architect.Tests.TestSupport.RichTextAndAutoNumberFixtures;

namespace D365Architect.Tests.Commands.Table;

/// <summary>
/// <c>table import --publish</c> as a user meets it (REQ-34, 35, 36, 37, 39):
/// <see cref="ImportTableCommand"/> through the real <see cref="ImportRunner"/>
/// and <see cref="TableImportService"/>, with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked.
///
/// Decisions pinned here, for the developer:
/// <list type="bullet">
/// <item>"The table doesn't exist" is answered by <c>IDataverseClient.EntityExistsAsync</c>,
/// before the import preview is built. These tests leave
/// <c>EntityDefinitionJson</c> unset for a missing table, so a run that builds the
/// preview first fails with "wasn't configured" instead of the REQ-35 message.</item>
/// <item>"Unapplied changes" (REQ-36) are what <c>table import</c> would actually write
/// (the same set as <see cref="TableImportPreview.HasChanges"/>): a table-level update
/// (<c>TableUpdateBody</c>) counts as one, and each column plan that is a Create, Update,
/// CreateLookupRelationship or CreateCustomerRelationship counts as one. NOT counted:
/// the informational YAML diff, live-only columns (WouldRemove), unsupported-type columns,
/// and the "seed ignored" warning on an otherwise unchanged column.</item>
/// <item>The refusal states the count: "1 unapplied change" / "2 unapplied changes".</item>
/// </list>
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a
/// prompt nobody answered fails the test, which is how <c>--yes</c> is shown
/// never to ask.
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

    private static AttributeDefinition SecondColumn() =>
        AttributeDefinitionBuilder.Create("String", "bogdan_Other", b => b.Name = "bogdan_other");

    private string WriteTableFile(string? displayName = null, params AttributeDefinition[] columns)
    {
        var path = Path.Combine(_directory, "tn_test.table.yml");
        File.WriteAllText(path, EntityYamlSerializer.ToYaml(new EntityDefinition { LogicalName = "tn_test", DisplayName = displayName, Attributes = columns }));
        return path;
    }

    private static (ImportTableCommand Command, FakeDataverseClient Client) CreateCommand(bool tableExistsLive, string? liveEntityJson = null)
    {
        var client = new FakeDataverseClient { EntityExists = _ => tableExistsLive, EntityDefinitionJson = liveEntityJson, EntityMetadataJson = "{}" };
        var service = new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));
        return (new ImportTableCommand(service, new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportTableCommand.Settings Settings(string input, bool yes = false, bool whatIf = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, Publish = true };

    /// <summary>A live table whose only column is the auto-number one, and a file that matches it exactly: nothing unapplied.</summary>
    private (ImportTableCommand Command, FakeDataverseClient Client, string Input) Build()
    {
        var (command, client) = CreateCommand(tableExistsLive: true, Entity(LiveAutoNumberString));
        return (command, client, WriteTableFile(null, AutoNumberColumn()));
    }

    // REQ-34: "`table import` ... accept `--publish`" - it is a real command-line option.
    // Code path: ImportTableCommand.Settings (via PublishableImportSettings.Publish) needs a [CommandOption("--publish")].
    [Fact]
    [Trait("Requirement", "REQ-34")]
    public async Task TableImport_OffersAPublishOption()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            TableCommands.Configure(config);
        });

        var exitCode = await app.RunAsync(["table", "import", "--help"]);

        Expect.Equal(0, exitCode, $"REQ-34: 'table import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--publish", console.Output, "REQ-34: 'table import' should offer --publish.");
    }

    // REQ-34 + REQ-37: the file matches what is live, so `table import --publish` publishes the table (whole, with its views and forms) and writes nothing.
    // Code path: ImportTableCommand.RunAsync in publish mode -> IDataverseClient.PublishEntityAsync.
    [Fact]
    [Trait("Requirement", "REQ-34")]
    public async Task RunAsync_PublishWhenTheFileMatchesLive_PublishesTheTableAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client, input) = Build();

        var exitCode = await command.RunAsync(Settings(input, yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-34: publishing an in-sync table should succeed. Output: <{console.Output}>");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "REQ-34/REQ-37: 'table import --publish' should publish the table named in the file, once.");
        Expect.Empty(client.PublishGlobalOptionSetsCalls, "REQ-34: a table publish should not publish any global choices.");
        PublishAssertions.NothingWritten(client, "REQ-34");
    }

    // REQ-35: "If what the file describes doesn't exist yet, `--publish` stops with an error saying to run the command without `--publish` first."
    // table import never creates the table (modify-exports-imports skill), so the advice is `table create`.
    // Code path: ImportTableCommand.RunAsync in publish mode, asking IDataverseClient.EntityExistsAsync before any preview.
    [Fact]
    [Trait("Requirement", "REQ-35")]
    public async Task RunAsync_PublishOfAMissingTable_SaysToRunTableCreateWithoutPublishFirstAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false); // EntityDefinitionJson unset: the preview must not be reached

        var exitCode = await command.RunAsync(Settings(WriteTableFile(null, AutoNumberColumn()), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-35: publishing a table that doesn't exist should exit with 1. Output: <{console.Output}>");
        Expect.Contains("tn_test doesn't exist yet", console.Output, "REQ-35: the user should be told which table doesn't exist yet.");
        Expect.Contains("table create", console.Output, "REQ-35: the user should be told to run 'table create'.");
        Expect.Contains("without --publish first", console.Output, "REQ-35: the user should be told to run it without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-35", "a missing table must not be published.");
        PublishAssertions.NothingWritten(client, "REQ-35");
    }

    // REQ-36 example: "File adds a column that isn't live -> 'the file has 1 unapplied change - run without --publish first', nothing published."
    // Code path: ImportTableCommand.RunAsync in publish mode, counting the column plans of TableImportPreview that HasChanges counts.
    [Fact]
    [Trait("Requirement", "REQ-36")]
    public async Task RunAsync_PublishWhenTheFileAddsAColumnThatIsNotLive_RefusesAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true, Entity()); // live table has no columns; the file adds one

        var exitCode = await command.RunAsync(Settings(WriteTableFile(null, AutoNumberColumn()), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-36: a file with an unapplied change should be refused with exit code 1. Output: <{console.Output}>");
        Expect.Contains("1 unapplied change", console.Output, "REQ-36: the refusal should say how many unapplied changes the file has.");
        Expect.Contains("without --publish first", console.Output, "REQ-36: the refusal should say to run the command without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-36", "publishing must not go ahead when the file differs from what is live.");
        PublishAssertions.NothingWritten(client, "REQ-36");
    }

    // REQ-36: the count is of changes, so two new columns are "2 unapplied changes".
    [Fact]
    [Trait("Requirement", "REQ-36")]
    public async Task RunAsync_PublishWhenTheFileAddsTwoColumns_SaysTwoUnappliedChanges()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true, Entity());

        var exitCode = await command.RunAsync(Settings(WriteTableFile(null, AutoNumberColumn(), SecondColumn()), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-36: a file with unapplied changes should be refused. Output: <{console.Output}>");
        Expect.Contains("2 unapplied changes", console.Output, "REQ-36: two new columns should be reported as 2 unapplied changes.");
        PublishAssertions.NothingPublished(client, "REQ-36", "nothing should be published when the file has unapplied changes.");
    }

    // Pinned (REQ-36): a table-level change (here a display name the live table lacks) is something the import would write, so it is unapplied too.
    [Fact]
    [Trait("Requirement", "REQ-36")]
    public async Task RunAsync_PublishWhenOnlyTheTableDisplayNameDiffers_RefusesAsAnUnappliedChange()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true, Entity(LiveAutoNumberString)); // live table has no DisplayName

        var exitCode = await command.RunAsync(Settings(WriteTableFile("Test table", AutoNumberColumn()), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-36: a changed table display name is an unapplied change. Output: <{console.Output}>");
        Expect.Contains("1 unapplied change", console.Output, "REQ-36: the table-level difference should count as one unapplied change.");
        PublishAssertions.NothingPublished(client, "REQ-36", "nothing should be published when the table-level properties differ.");
    }

    // Pinned (REQ-36): live columns the file doesn't mention are never imported or deleted, so they are not "unapplied changes". The table publishes.
    [Fact]
    [Trait("Requirement", "REQ-36")]
    public async Task RunAsync_PublishWhenTheTableHasLiveOnlyColumns_StillPublishes()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true, Entity(LiveAutoNumberString, LiveRichTextMemo)); // bogdan_notes is live only

        var exitCode = await command.RunAsync(Settings(WriteTableFile(null, AutoNumberColumn()), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-36: live-only columns must not block a publish. Output: <{console.Output}>");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "REQ-36: the table should be published despite its live-only column.");
    }

    // Pinned (REQ-36): the "seed ignored" note is a warning on an unchanged column, not a change the tool would write. The table publishes.
    [Fact]
    [Trait("Requirement", "REQ-36")]
    public async Task RunAsync_PublishWhenOnlyASeedIsIgnored_StillPublishes()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true, Entity(LiveAutoNumberString));

        var exitCode = await command.RunAsync(Settings(WriteTableFile(null, AutoNumberColumn(seed: 1)), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-36: an ignored seed must not block a publish. Output: <{console.Output}>");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "REQ-36: the table should be published; the ignored seed is only a warning.");
    }

    // REQ-39 example: `--publish --whatif` prints "would publish: tn_test"; nothing is published and nothing is prompted.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithWhatIf_PrintsWouldPublishTheTableAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client, input) = Build();

        var exitCode = await command.RunAsync(Settings(input, whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: --publish --whatif should succeed. Output: <{console.Output}>");
        Expect.Contains("would publish: tn_test", console.Output, "REQ-39: --whatif should say what it would publish.");
        PublishAssertions.NothingPublished(client, "REQ-39", "--whatif writes nothing, including no publish.");
    }

    // REQ-39: "asks first" - answering no publishes nothing and exits 0. Proves the prompt exists: without one this would publish.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_DecliningThePromptPublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client, input) = Build();

        var exitCode = await command.RunAsync(Settings(input), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: declining the prompt should exit 0. Output: <{console.Output}>");
        Expect.Contains("would publish: tn_test", console.Output, "REQ-39: the run should show what it is about to publish before asking.");
        PublishAssertions.NothingPublished(client, "REQ-39", "answering no must not publish.");
    }

    // REQ-39: answering yes publishes.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_AcceptingThePromptPublishesTheTable()
    {
        using var console = new ConsoleHarness("y");
        var (command, client, input) = Build();

        var exitCode = await command.RunAsync(Settings(input), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: confirming should publish and exit 0. Output: <{console.Output}>");
        Expect.Sequence(["tn_test"], client.PublishEntityCalls, "REQ-39: answering yes should publish the table once.");
    }
}

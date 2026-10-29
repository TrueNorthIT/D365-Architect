using D365Architect.Commands;
using D365Architect.Commands.Choice;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands.Choice;

/// <summary>
/// <c>choice import --publish</c> as a user meets it (REQ-34, 35, 36, 38, 39):
/// <see cref="ImportChoiceCommand"/> through the real <see cref="ImportRunner"/>
/// and <see cref="GlobalChoiceImportService"/>, with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked.
///
/// Decisions pinned here, for the developer:
/// <list type="bullet">
/// <item>A global choice is published by name through the new
/// <c>IDataverseClient.PublishGlobalOptionSetsAsync</c> (one call, every choice of the
/// file, in file order) - never through <c>PublishEntityAsync</c>.</item>
/// <item>A choice "doesn't exist" when its plan is <see cref="GlobalChoiceImportAction.Create"/>
/// (the live lookup found nothing). The refusal names every such choice and says to run
/// <c>choice import</c> without --publish first.</item>
/// <item>"Unapplied changes" are the plans <see cref="GlobalChoicesImportPreview.HasChanges"/> counts
/// as an Update. These tests only pin the count for one changed choice ("1 unapplied change").</item>
/// </list>
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a
/// prompt nobody answered fails the test, which is how <c>--yes</c> is shown
/// never to ask.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportChoiceCommandPublishTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportChoiceCommandPublishTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static GlobalChoiceDefinition Choice(string name, string? description = null) => new()
    {
        Name = name,
        Description = description,
        Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }],
    };

    /// <summary>What Dataverse returns for a live choice that matches <see cref="Choice"/> (no description) exactly.</summary>
    private static string LiveJson(string name) =>
        $$"""{ "MetadataId": "11111111-1111-1111-1111-111111111111", "Name": "{{name}}", "Options": [ { "Value": 1, "Label": { "UserLocalizedLabel": { "Label": "A" } } } ] }""";

    private string WriteChoices(params GlobalChoiceDefinition[] choices)
    {
        var path = Path.Combine(_directory, "choices.choice.yml");
        File.WriteAllText(path, GlobalChoiceYamlSerializer.ToYaml(choices));
        return path;
    }

    /// <summary>A fake environment where only the named choices exist live, each matching <see cref="Choice"/>.</summary>
    private static (ImportChoiceCommand Command, FakeDataverseClient Client) CreateCommand(params string[] liveChoiceNames)
    {
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = name => liveChoiceNames.Contains(name) ? LiveJson(name) : null };
        return (new ImportChoiceCommand(new GlobalChoiceImportService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportChoiceCommand.Settings Settings(string input, bool yes = false, bool whatIf = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, Publish = true };

    // REQ-34: "`choice import` ... accept `--publish`" - it is a real command-line option.
    // Code path: ImportChoiceCommand.Settings (via PublishableImportSettings.Publish) needs a [CommandOption("--publish")].
    [Fact]
    [Trait("Requirement", "REQ-34")]
    public async Task ChoiceImport_OffersAPublishOption()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            ChoiceCommands.Configure(config);
        });

        var exitCode = await app.RunAsync(["choice", "import", "--help"]);

        Expect.Equal(0, exitCode, $"REQ-34: 'choice import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--publish", console.Output, "REQ-34: 'choice import' should offer --publish.");
    }

    // REQ-38: "`choice import --publish` publishes every global choice in the file" - in one call, in file order, and only the choices (no table); REQ-34: nothing else is written.
    // Code path: ImportChoiceCommand.RunAsync in publish mode -> IDataverseClient.PublishGlobalOptionSetsAsync.
    [Fact]
    [Trait("Requirement", "REQ-38")]
    public async Task RunAsync_PublishWhenEveryChoiceExists_PublishesAllOfThemInFileOrderAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client) = CreateCommand("bogdan_status", "bogdan_priority", "bogdan_rating");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"), Choice("bogdan_priority"), Choice("bogdan_rating")), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-38: publishing choices that all exist should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "REQ-38: all the file's choices should be published in a single PublishXml call.");
        var call = client.PublishGlobalOptionSetsCalls[0];
        Expect.Sequence(["bogdan_status", "bogdan_priority", "bogdan_rating"], call, "REQ-38: every global choice in the file should be published, in file order, in one call.");
        Expect.Empty(client.PublishEntityCalls, "REQ-38: publishing choices should not publish any table.");
        PublishAssertions.NothingWritten(client, "REQ-34");
    }

    // REQ-38 example: "3 choices, 1 missing -> refusal names it, nothing published."
    // Code path: ImportChoiceCommand.RunAsync in publish mode, treating GlobalChoiceImportAction.Create as "doesn't exist".
    [Fact]
    [Trait("Requirement", "REQ-38")]
    public async Task RunAsync_PublishWhenOneOfThreeChoicesIsMissing_RefusesNamingItAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand("bogdan_status", "bogdan_rating"); // bogdan_priority is not live

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"), Choice("bogdan_priority"), Choice("bogdan_rating")), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-38: a file with a missing choice should be refused with exit code 1. Output: <{console.Output}>");
        Expect.Contains("bogdan_priority", console.Output, "REQ-38: the refusal should name the choice that doesn't exist.");
        Expect.Contains("doesn't exist yet", console.Output, "REQ-35/REQ-38: the refusal should say the choice doesn't exist yet.");
        Expect.Contains("choice import", console.Output, "REQ-35: the refusal should say to run 'choice import'.");
        Expect.Contains("without --publish first", console.Output, "REQ-35: the refusal should say to run it without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-38", "no choice should be published when any in the file is missing.");
        PublishAssertions.NothingWritten(client, "REQ-38");
    }

    // REQ-38: "refuses and names them" - every missing choice is named.
    [Fact]
    [Trait("Requirement", "REQ-38")]
    public async Task RunAsync_PublishWhenTwoChoicesAreMissing_NamesBothOfThem()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand("bogdan_rating");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"), Choice("bogdan_priority"), Choice("bogdan_rating")), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-38: a file with missing choices should be refused. Output: <{console.Output}>");
        Expect.Contains("bogdan_status", console.Output, "REQ-38: the refusal should name the first missing choice.");
        Expect.Contains("bogdan_priority", console.Output, "REQ-38: the refusal should name the second missing choice.");
        PublishAssertions.NothingPublished(client, "REQ-38", "no choice should be published when any in the file is missing.");
    }

    // REQ-36: a choice whose live definition differs from the file would be updated by a plain import - refuse rather than publish something other than the file.
    // Code path: ImportChoiceCommand.RunAsync in publish mode, using GlobalChoicesImportPreview.HasChanges.
    [Fact]
    [Trait("Requirement", "REQ-36")]
    public async Task RunAsync_PublishWhenAChoiceDiffersFromLive_RefusesAndPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand("bogdan_status", "bogdan_priority");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"), Choice("bogdan_priority", description: "Now described")), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-36: a file with an unapplied change should be refused with exit code 1. Output: <{console.Output}>");
        Expect.Contains("1 unapplied change", console.Output, "REQ-36: the refusal should say how many unapplied changes the file has.");
        Expect.Contains("without --publish first", console.Output, "REQ-36: the refusal should say to run the command without --publish first.");
        PublishAssertions.NothingPublished(client, "REQ-36", "publishing must not go ahead when the file differs from what is live.");
        PublishAssertions.NothingWritten(client, "REQ-36");
    }

    // REQ-39 example: `--publish --whatif` prints "would publish: <name>"; nothing is published and nothing is prompted.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithWhatIf_PrintsWouldPublishTheChoicesAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand("bogdan_status", "bogdan_priority");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"), Choice("bogdan_priority")), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: --publish --whatif should succeed. Output: <{console.Output}>");
        Expect.Contains("would publish: ", console.Output, "REQ-39: --whatif should say what it would publish.");
        Expect.Contains("bogdan_status", console.Output, "REQ-39: --whatif should name the first choice it would publish.");
        Expect.Contains("bogdan_priority", console.Output, "REQ-39: --whatif should name the second choice it would publish.");
        PublishAssertions.NothingPublished(client, "REQ-39", "--whatif writes nothing, including no publish.");
    }

    // REQ-39: "asks first" - answering no publishes nothing and exits 0. Proves the prompt exists: without one this would publish.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_DecliningThePromptPublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand("bogdan_status");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: declining the prompt should exit 0. Output: <{console.Output}>");
        Expect.Contains("would publish: bogdan_status", console.Output, "REQ-39: the run should show what it is about to publish before asking.");
        PublishAssertions.NothingPublished(client, "REQ-39", "answering no must not publish.");
    }

    // REQ-39: answering yes publishes.
    [Fact]
    [Trait("Requirement", "REQ-39")]
    public async Task RunAsync_PublishWithoutYes_AcceptingThePromptPublishesTheChoices()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand("bogdan_status");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-39: confirming should publish and exit 0. Output: <{console.Output}>");
        Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "REQ-39: answering yes should publish the choice in one call.");
        Expect.Sequence(["bogdan_status"], client.PublishGlobalOptionSetsCalls[0], "REQ-39: answering yes should publish the choice.");
    }
}

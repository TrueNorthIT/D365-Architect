using D365Architect.Commands;
using D365Architect.Commands.Choice;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.Choice;

/// <summary>
/// <c>choice import</c> publishes after it applies changes (REQ-2, 3, 4, 6 of the "publish by default"
/// requirements, tagged with the "Feature" trait "publish-by-default" because those IDs overlap older ones):
/// <see cref="ImportChoiceCommand"/> through the real <see cref="ImportRunner"/> and
/// <see cref="GlobalChoiceImportService"/>, with only Dataverse (<see cref="FakeDataverseClient"/>), sign-in
/// and the console faked. The old <c>--publish</c> publish-only mode these tests used to cover is gone.
///
/// A global choice is published by name through <c>IDataverseClient.PublishGlobalOptionSetsAsync</c> (one
/// call), never through <c>PublishEntityAsync</c>. Which of a file's choices are published when only some
/// changed is deliberately not pinned here; these tests use a single choice.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered
/// fails the test, which is how a single confirmation (no second prompt for the publish) is pinned.
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

    /// <summary>A fake environment where only the named choices exist live, each matching <see cref="Choice"/> (no description).</summary>
    private static (ImportChoiceCommand Command, FakeDataverseClient Client) CreateCommand(params string[] liveChoiceNames)
    {
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = name => liveChoiceNames.Contains(name) ? LiveJson(name) : null };
        return (new ImportChoiceCommand(new GlobalChoiceImportService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportChoiceCommand.Settings Settings(string input, bool yes = false, bool whatIf = false, bool noPublish = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, NoPublish = noPublish };

    // REQ-2: "`choice import` ... publish after applying changes." - a new choice is created, then published by name.
    // Code path: ImportChoiceCommand.RunAsync -> ImportRunner flow: after GlobalChoiceImportService.ApplyAsync, IGlobalChoiceImportService.PublishAsync -> IDataverseClient.PublishGlobalOptionSetsAsync.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_CreatingAChoice_PublishesIt()
    {
        using var console = new ConsoleHarness(); // --yes: a prompt would fail the test
        var (command, client) = CreateCommand(); // bogdan_status is not live

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status")), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-2: importing a new choice should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateGlobalOptionSetCalls.Count, "REQ-2: the new choice should be created.");
        Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "REQ-2: the changed choice should be published in a single PublishXml call.");
        Expect.Sequence(["bogdan_status"], client.PublishGlobalOptionSetsCalls.FirstOrDefault() ?? [], "REQ-2: the choice that was created should be the one published.");
        Expect.Empty(client.PublishEntityCalls, "REQ-2: publishing a choice should not publish any table.");
    }

    // REQ-2: an existing choice that is updated is published too.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_UpdatingAChoice_PublishesIt()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand("bogdan_status");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status", description: "Now described")), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-2: updating a choice should succeed. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateGlobalOptionSetCalls.Count, "REQ-2: the changed choice should be updated.");
        Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "REQ-2: the updated choice should be published in a single PublishXml call.");
        Expect.Sequence(["bogdan_status"], client.PublishGlobalOptionSetsCalls.FirstOrDefault() ?? [], "REQ-2: the choice that was updated should be the one published.");
    }

    // REQ-2 + the confirmation: answering yes to "Import these changes?" is the only question - publishing follows without asking again.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ConfirmingTheImport_PublishesWithoutAskingAgain()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-2: confirming once should import and publish. Output: <{console.Output}>");
        Expect.Equal(1, client.PublishGlobalOptionSetsCalls.Count, "REQ-2: the choice should be published after the single confirmation.");
    }

    // REQ-3: "If nothing changed, nothing is published."
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_FileMatchesLive_PublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand("bogdan_status");

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status")), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-3: an in-sync choice is not an error. Output: <{console.Output}>");
        PublishAssertions.NothingPublished(client, "REQ-3", "nothing changed, so nothing should be published.");
    }

    // REQ-3: answering no applies nothing, so nothing is published.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_DecliningTheImport_PublishesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status"))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"Declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.CreateGlobalOptionSetCalls, "Declining should write nothing.");
        PublishAssertions.NothingPublished(client, "REQ-3", "declining wrote nothing, so there is nothing to publish.");
    }

    // REQ-4: "`--no-publish` skips publishing" - the choice is still created.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithNoPublish_AppliesTheChangeButPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status")), yes: true, noPublish: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-4: --no-publish should still import. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateGlobalOptionSetCalls.Count, "REQ-4: --no-publish should still write the change.");
        PublishAssertions.NothingPublished(client, "REQ-4", "--no-publish skips publishing.");
    }

    // REQ-6: "`--whatif` never publishes."
    [Fact]
    [Trait("Requirement", "REQ-6")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_WithWhatIf_WritesAndPublishesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteChoices(Choice("bogdan_status")), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-6: --whatif should succeed. Output: <{console.Output}>");
        Expect.Empty(client.CreateGlobalOptionSetCalls, "REQ-6: --whatif must not write.");
        PublishAssertions.NothingPublished(client, "REQ-6", "--whatif never publishes.");
    }
}

using D365Architect.Commands;
using D365Architect.Commands.Form;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Commands.Form;

/// <summary>
/// <c>form import</c> and publishing (REQ-3, 4 of the "publish by default" requirements, tagged with the
/// "Feature" trait "publish-by-default" because those IDs overlap older ones): it publishes after applying,
/// as it always has (pinned in <c>ImportFormCommandCreateTests</c>), and now <c>--no-publish</c> turns that
/// off. <see cref="ImportFormCommand"/> through the real <see cref="ImportRunner"/> and
/// <see cref="FormImportService"/>, with only Dataverse (<see cref="FakeDataverseClient"/>), sign-in and the
/// console faked. <c>--whatif</c> never publishing (REQ-6) is already pinned there too
/// (<c>RunAsync_NewFormWithWhatIf_ShowsTheCreateLineAndWritesNothing</c>).
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered
/// fails the test.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportFormCommandPublishTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportFormCommandPublishTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteForm(FormDefinition form)
    {
        var path = Path.Combine(_directory, "form.form.yml");
        File.WriteAllText(path, FormYamlSerializer.ToYaml(form));
        return path;
    }

    private static (ImportFormCommand Command, FakeDataverseClient Client) CreateCommand()
    {
        var client = ClientWithTable();
        AddFreshTableForms(client);
        return (new ImportFormCommand(new FormImportService(client, new FormJsonDefinitionReader()), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportFormCommand.Settings Settings(string input, bool yes = false, bool noPublish = false) =>
        new() { Input = input, Yes = yes, NoPublish = noPublish };

    // REQ-4: "`--no-publish` skips publishing on ... `form import`." - the new form is still created.
    // Code path: ImportFormCommand's ApplyAsync (today always followed by IFormImportService.PublishAsync) skipping the publish when settings.NoPublish.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_NewFormWithNoPublish_CreatesTheFormButPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre")), yes: true, noPublish: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-4: --no-publish should still import the form. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSystemFormCalls.Count, "REQ-4: --no-publish should still create the form.");
        PublishAssertions.NothingPublished(client, "REQ-4", "--no-publish skips publishing on form import.");
    }

    // REQ-4: the same for an existing form that is updated.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_ExistingFormWithNoPublish_UpdatesTheFormButPublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Information", label: "Information, edited")), yes: true, noPublish: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-4: --no-publish should still update the form. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSystemFormXmlCalls.Count, "REQ-4: --no-publish should still update the form.");
        PublishAssertions.NothingPublished(client, "REQ-4", "--no-publish skips publishing on form import.");
    }

    // REQ-3: "If nothing changed, nothing is published." - a form whose rebuilt FormXML already matches what is live is a no-op, so no publish.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Feature", "publish-by-default")]
    public async Task RunAsync_FormAlreadyMatchesLive_PublishesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();
        var live = FormOf("Invoice Centre");
        client.LiveSystemForms.Add((new ExistingSystemForm(Guid.NewGuid(), FormXmlWriter.Write(live), TableName, "Invoice Centre"), MainType));

        var exitCode = await command.RunAsync(Settings(WriteForm(live), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-3: an in-sync form is not an error. Output: <{console.Output}>");
        Expect.Empty(client.UpdateSystemFormXmlCalls, "REQ-3: an unchanged form should not be rewritten.");
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

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre"))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"Declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.CreateSystemFormCalls, "Declining should write nothing.");
        PublishAssertions.NothingPublished(client, "REQ-3", "declining wrote nothing, so there is nothing to publish.");
    }
}

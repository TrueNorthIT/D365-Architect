using D365Architect.Commands;
using D365Architect.Commands.Form;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Commands.Form;

/// <summary>
/// <c>form import</c> as a user meets it, for REQ-22 (<c>--solution</c>), REQ-29
/// (a form matching nothing by name and type is created: shown in the plan,
/// confirmed; the table's "Information" forms are left alone), REQ-31 (two
/// matches refuse and list both ids) and REQ-33 (created forms are published):
/// <see cref="ImportFormCommand"/> through the real <see cref="ImportRunner"/>
/// and <see cref="FormImportService"/>, with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked. The fake's
/// live forms are a fresh table's three "Information" forms.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a
/// prompt nobody answered fails the test.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportFormCommandCreateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportFormCommandCreateTests() => Directory.CreateDirectory(_directory);

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

    private static ImportFormCommand.Settings Settings(string input, bool yes = false, bool whatIf = false, string? solution = null) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, Solution = solution };

    // ---- REQ-29: the plan shows the create; you confirm ----

    // REQ-29 example: `form: Invoice Centre` on a new table -> `+ form 'Invoice Centre' (main, create)`; "Information" untouched.
    // --whatif shows the plan and writes nothing. Code path: ImportFormCommand's ImportFlowSpec.PrintChanges for a FormImportPreview with IsCreate.
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task RunAsync_NewFormWithWhatIf_ShowsTheCreateLineAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre")), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-29: --whatif on a new form should succeed. Output: <{console.Output}>");
        Expect.Contains("+ form 'Invoice Centre' (main, create)", console.Output, "REQ-29: the plan should show the form as a create of type main.");
        Expect.Empty(client.CreateSystemFormCalls, "REQ-29: --whatif must not create the form.");
        Expect.Empty(client.PublishEntityCalls, "REQ-29: --whatif must not publish.");
    }

    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task RunAsync_NewFormWithoutYes_AcceptingTheConfirmationCreatesTheForm()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre"))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-29: confirming should create the form and exit 0. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSystemFormCalls.Count, "REQ-29: answering yes should create the form once.");
        Expect.Empty(client.UpdateSystemFormXmlCalls, "REQ-29: the existing 'Information' forms should be left alone.");
    }

    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task RunAsync_NewFormWithoutYes_DecliningTheConfirmationWritesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre"))), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-29: declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.CreateSystemFormCalls, "REQ-29: answering no must not create the form.");
        Expect.Empty(client.PublishEntityCalls, "REQ-29: and must not publish.");
    }

    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task RunAsync_NewFormWithYes_CreatesWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: a prompt would fail the test
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre")), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-29: --yes should create without asking. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSystemFormCalls.Count, "REQ-29: --yes should create the form.");
    }

    // A non-main form: the plan says which type it is, in lower case, as the requirement's example does for main.
    [Fact]
    [Trait("Requirement", "REQ-29")]
    public async Task RunAsync_NewCardForm_PlanNamesTheType()
    {
        using var console = new ConsoleHarness();
        var (command, _) = CreateCommand();

        await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre Card", type: "Card")), whatIf: true), CancellationToken.None);

        Expect.Contains("+ form 'Invoice Centre Card' (card, create)", console.Output, "REQ-29: the plan should show the form's type (the lower-cased label) next to 'create'.");
    }

    // ---- REQ-33: created forms are published ----

    // REQ-33: "Created forms are published, as form import already does."
    [Fact]
    [Trait("Requirement", "REQ-33")]
    public async Task RunAsync_NewForm_IsPublishedAfterTheCreate()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre")), yes: true), CancellationToken.None);

        Expect.Sequence([TableName], client.PublishEntityCalls, "REQ-33: after the form is created its table should be published.");
    }

    // ---- REQ-22: --solution ----

    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task RunAsync_NewFormWithSolution_CreatesItInThatSolution()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        await command.RunAsync(Settings(WriteForm(FormOf("Invoice Centre")), yes: true, solution: "TableCreateTest"), CancellationToken.None);

        Expect.Equal("TableCreateTest", Assert.Single(client.CreateSystemFormCalls).SolutionUniqueName, "REQ-22: '--solution TableCreateTest' should create the form inside that solution.");
    }

    // REQ-22 (change half) and "existing update behaviour must not regress": "Information" of type main now matches exactly one form and is updated - and publishes, as before.
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task RunAsync_ExistingFormWithSolution_UpdatesItInThatSolutionAndPublishes()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Information", label: "Information, edited")), yes: true, solution: "TableCreateTest"), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-22: updating the main 'Information' form should succeed. Output: <{console.Output}>");
        var call = Assert.Single(client.UpdateSystemFormXmlCalls);
        Expect.Equal(MainInformationId, call.FormId, "REQ-29: the main 'Information' form should be the one updated.");
        Expect.Equal("TableCreateTest", call.SolutionUniqueName, "REQ-22: '--solution' should reach the update of an existing form.");
        Expect.Empty(client.CreateSystemFormCalls, "REQ-29: an existing form must be updated, not created again.");
        Expect.Sequence([TableName], client.PublishEntityCalls, "REQ-33: an updated form is published, as before.");
    }

    // REQ-22: the option exists on the command line.
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task FormImport_OffersASolutionOption()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            FormCommands.Configure(config);
        });

        var exitCode = await app.RunAsync(["form", "import", "--help"]);

        Expect.Equal(0, exitCode, $"REQ-22: 'form import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--solution", console.Output, "REQ-22: 'form import' should offer --solution.");
    }

    // ---- REQ-31: ambiguity is refused, listing the candidates ----

    // REQ-31 example: Two main forms named "Information" -> refusal listing both ids; nothing is written.
    [Fact]
    [Trait("Requirement", "REQ-31")]
    public async Task RunAsync_TwoMainFormsWithTheSameName_ListsBothIdsAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();
        var second = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        client.LiveSystemForms.Add(InformationForm(second, MainType));

        var exitCode = await command.RunAsync(Settings(WriteForm(FormOf("Information")), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-31: an ambiguous match should be refused with exit code 1. Output: <{console.Output}>");
        Expect.Contains(MainInformationId.ToString(), console.Output, "REQ-31: the refusal should list the first matching form's id.");
        Expect.Contains(second.ToString(), console.Output, "REQ-31: the refusal should list the second matching form's id.");
        Expect.Empty(client.CreateSystemFormCalls, "REQ-31: nothing should be created.");
        Expect.Empty(client.UpdateSystemFormXmlCalls, "REQ-31: nothing should be updated.");
    }

    // ---- the input file is never rewritten ----

    [Theory]
    [Trait("Requirement", "REQ-29")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LeavesTheInputFileByteIdentical(bool whatIf)
    {
        using var console = new ConsoleHarness();
        var (command, _) = CreateCommand();
        var path = WriteForm(FormOf("Invoice Centre"));
        var before = File.ReadAllBytes(path);

        await command.RunAsync(Settings(path, yes: !whatIf, whatIf: whatIf), CancellationToken.None);

        Expect.True(before.SequenceEqual(File.ReadAllBytes(path)), $"REQ-29: the form file should be byte-identical after 'form import' (whatIf: {whatIf}).");
    }
}

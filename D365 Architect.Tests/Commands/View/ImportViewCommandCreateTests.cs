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
/// <c>view import</c> as a user meets it, for REQ-22 (<c>--solution</c>), REQ-23
/// (a view matching nothing is created: shown in the plan, confirmed, same as a
/// new column), REQ-28 (both <c>columns</c> and <c>layoutXml</c> is refused with
/// a message) and REQ-33 (a created view is not published, as today):
/// <see cref="ImportViewCommand"/> through the real <see cref="ImportRunner"/>
/// and <see cref="ViewImportService"/>, with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked. Files use
/// a raw <c>layoutXml</c> so these tests don't depend on the <c>columns</c>
/// converter; the <c>columns</c> behaviour has its own tests.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a
/// prompt nobody answered fails the test, which is how <c>--yes</c> is shown
/// never to ask.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportViewCommandCreateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportViewCommandCreateTests() => Directory.CreateDirectory(_directory);

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

    private static ViewDefinition RawView(string name = "All Invoice Centres") => ViewOf(name: name, fetchXml: ExportedFetchXml, layoutXml: ExportedLayoutXml());

    private static (ImportViewCommand Command, FakeDataverseClient Client) CreateCommand(FakeDataverseClient? client = null)
    {
        client ??= ClientWithTable();
        return (new ImportViewCommand(new ViewImportService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    private static ImportViewCommand.Settings Settings(string input, bool yes = false, bool whatIf = false, string? solution = null) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, Solution = solution };

    // ---- REQ-23: the plan shows the create; you confirm ----

    // REQ-23 example: `+ view 'All Invoice Centres' (create)`. --whatif shows the plan and writes nothing.
    // Code path: ImportViewCommand's ImportFlowSpec.PrintChanges for a ViewImportPreview with IsCreate.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task RunAsync_NewViewWithWhatIf_ShowsTheCreateLineAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteView(RawView()), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-23: --whatif on a new view should succeed. Output: <{console.Output}>");
        Expect.Contains("+ view 'All Invoice Centres' (create)", console.Output, "REQ-23: the plan should show the view as a create.");
        Expect.Empty(client.CreateSavedQueryCalls, "REQ-23: --whatif must not create the view.");
    }

    // REQ-23: "the plan shows it and you confirm" - yes creates it.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task RunAsync_NewViewWithoutYes_AcceptingTheConfirmationCreatesTheView()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteView(RawView())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-23: confirming should create the view and exit 0. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSavedQueryCalls.Count, "REQ-23: answering yes to the confirmation should create the view once.");
    }

    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task RunAsync_NewViewWithoutYes_DecliningTheConfirmationWritesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteView(RawView())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-23: declining should exit 0, as every import does. Output: <{console.Output}>");
        Expect.Empty(client.CreateSavedQueryCalls, "REQ-23: answering no must not create the view.");
    }

    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task RunAsync_NewViewWithYes_CreatesWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: a prompt would fail the test
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteView(RawView()), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-23: --yes should create without asking. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateSavedQueryCalls.Count, "REQ-23: --yes should create the view.");
    }

    // ---- REQ-22: --solution ----

    // REQ-22 example: `view import -i all.view.yml --solution TableCreateTest` -> the view appears in the solution.
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task RunAsync_NewViewWithSolution_CreatesItInThatSolution()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        await command.RunAsync(Settings(WriteView(RawView()), yes: true, solution: "TableCreateTest"), CancellationToken.None);

        Expect.Equal("TableCreateTest", Assert.Single(client.CreateSavedQueryCalls).SolutionUniqueName, "REQ-22: '--solution TableCreateTest' should create the view inside that solution.");
    }

    // REQ-22: the change half - an existing view that is updated joins the solution as well.
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task RunAsync_ExistingViewWithSolution_UpdatesItInThatSolution()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithTable();
        client.SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), null, "<fetch>old</fetch>", ExportedLayoutXml());
        var (command, _) = CreateCommand(client);

        var exitCode = await command.RunAsync(Settings(WriteView(RawView()), yes: true, solution: "TableCreateTest"), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-22: updating an existing view should succeed. Output: <{console.Output}>");
        Expect.Equal("TableCreateTest", Assert.Single(client.UpdateSavedQueryCalls).SolutionUniqueName, "REQ-22: '--solution' should reach the update of an existing view.");
        Expect.Empty(client.CreateSavedQueryCalls, "REQ-22: an existing view must be updated, not created again.");
    }

    // REQ-22: the option exists on the command line.
    // Code path: ImportViewCommand.Settings.Solution needs a [CommandOption("-s|--solution <UNIQUE_NAME>")], as CreateTableCommand has.
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task ViewImport_OffersASolutionOption()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            ViewCommands.Configure(config);
        });

        var exitCode = await app.RunAsync(["view", "import", "--help"]);

        Expect.Equal(0, exitCode, $"REQ-22: 'view import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--solution", console.Output, "REQ-22: 'view import' should offer --solution.");
    }

    // ---- REQ-33: a created view is not published ----

    [Fact]
    [Trait("Requirement", "REQ-33")]
    public async Task RunAsync_NewView_IsNotPublished()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        await command.RunAsync(Settings(WriteView(RawView()), yes: true), CancellationToken.None);

        Expect.Equal(1, client.CreateSavedQueryCalls.Count, "Precondition: the view should have been created.");
        Expect.Empty(client.PublishEntityCalls, "REQ-33: created views are not published, as view import never has.");
    }

    // ---- REQ-28: refusal reaches the user ----

    // REQ-28 example: Both given -> "use either columns or layoutXml, not both". Written as YAML by hand, as a user would.
    [Fact]
    [Trait("Requirement", "REQ-28")]
    public async Task RunAsync_ColumnsAndLayoutXmlBothGiven_SaysUseEitherAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();
        var path = Path.Combine(_directory, "both.view.yml");
        File.WriteAllText(path, string.Join("\n",
            "view: All Invoice Centres",
            $"entity: {TableName}",
            "columns:",
            "- bogdan_name",
            "layoutXml: |",
            "  " + ExportedLayoutXml(),
            "") );

        var exitCode = await command.RunAsync(Settings(path, yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-28: a file giving both should be refused with exit code 1. Output: <{console.Output}>");
        Expect.Contains("use either columns or layoutXml, not both", console.Output, "REQ-28: the user should be told to use one or the other.");
        Expect.Empty(client.CreateSavedQueryCalls, "REQ-28: nothing should be written when the file is refused.");
        Expect.Empty(client.UpdateSavedQueryCalls, "REQ-28: nothing should be written when the file is refused.");
    }

    // A create refusal from the service (here: nothing to build a layout from) is shown as a message, not a stack trace.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task RunAsync_NewViewThatCantBeBuilt_ShowsTheReasonAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand();

        var exitCode = await command.RunAsync(Settings(WriteView(ViewOf(fetchXml: ExportedFetchXml)), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, $"REQ-23: a new view with neither columns nor layoutXml should be refused with exit code 1. Output: <{console.Output}>");
        Expect.Contains("All Invoice Centres", console.Output, "REQ-23: the refusal should name the view.");
        Expect.Contains("columns", console.Output, "REQ-23: the refusal should say a new view needs 'columns' (or a layoutXml), not just that it wasn't found.");
        Expect.Empty(client.CreateSavedQueryCalls, "REQ-23: nothing should be written when the file is refused.");
    }

    // ---- existing behaviour that must not regress ----

    // The input file is never rewritten, created or not (modify-exports-imports skill; REQ-11 of the table-create work).
    [Theory]
    [Trait("Requirement", "REQ-23")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LeavesTheInputFileByteIdentical(bool whatIf)
    {
        using var console = new ConsoleHarness();
        var (command, _) = CreateCommand();
        var path = WriteView(RawView());
        var before = File.ReadAllBytes(path);

        await command.RunAsync(Settings(path, yes: !whatIf, whatIf: whatIf), CancellationToken.None);

        Expect.True(before.SequenceEqual(File.ReadAllBytes(path)), $"REQ-23: the view file should be byte-identical after 'view import' (whatIf: {whatIf}).");
    }
}

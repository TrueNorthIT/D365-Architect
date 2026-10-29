using D365Architect.Commands;
using D365Architect.Commands.Table;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands.Table;

/// <summary>
/// Covers <c>table create</c> as a user meets it: <see cref="CreateTableCommand"/>
/// run through the real <see cref="ImportRunner"/> flow and the real
/// <see cref="TableCreateService"/>, with only Dataverse
/// (<see cref="FakeDataverseClient"/>), sign-in and the console faked. What it
/// prints, what it writes, whether it asks first, and that it leaves the
/// input file alone (REQ-1, 4-8, 11, 12).
///
/// Sits in the "Console" collection because it swaps the process-wide
/// <c>AnsiConsole.Console</c>; see <see cref="ConsoleHarness"/>. Prompts are
/// answered by the harness, and a prompt nobody answered fails the test - that
/// is how <c>--yes</c> is shown never to ask.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class CreateTableCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public CreateTableCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteTableFile(string yaml)
    {
        var path = Path.Combine(_directory, "tn_project.table.yml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static (CreateTableCommand Command, FakeDataverseClient Client) CreateCommand(bool tableExistsLive)
    {
        var client = new FakeDataverseClient { EntityExists = _ => tableExistsLive };
        var command = new CreateTableCommand(new TableCreateService(client), new ImportRunner(new FakeAuthenticationService()));
        return (command, client);
    }

    private static CreateTableCommand.Settings Settings(string input, bool yes = false, bool whatIf = false, string? solution = null) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf, Solution = solution };

    // ---- REQ-1: the command exists ----

    // REQ-1: "A new `table create` command" - it is registered under the `table` branch with the options the requirements use.
    // Code path: TableCommands.Configure registering CreateTableCommand as "create".
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task TableCreate_IsRegisteredWithInputSolutionYesAndWhatIfOptions()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            TableCommands.Configure(config);
        });

        var exitCode = await app.RunAsync(["table", "create", "--help"]);

        Expect.Equal(0, exitCode, $"REQ-1: 'table create --help' should succeed, which needs the command registered under 'table'. Output: <{console.Output}>");
        foreach (var option in new[] { "--input", "--solution", "--yes", "--whatif" })
        {
            Expect.Contains(option, console.Output, $"REQ-1: 'table create' should offer {option}.");
        }
    }

    // REQ-1 example: `table create --input tn_project.table.yml --solution TNCore` on an environment without `tn_project`: table exists afterwards.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task RunAsync_TableMissing_YesCreatesItWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: a prompt would fail the test
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-1: creating a missing table with --yes should succeed. Output: <{console.Output}>");
        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal("tn_Project", (string?)call.Body["SchemaName"], "REQ-1: the table from the file should be created.");
    }

    // ---- REQ-8: --solution reaches the create ----

    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task RunAsync_WithSolution_CreatesTheTableInThatSolution()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true, solution: "TNCore"), CancellationToken.None);

        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal("TNCore", call.SolutionUniqueName, "REQ-8: '--solution TNCore' should create the table inside TNCore.");
    }

    // ---- REQ-4 / REQ-5: refusals ----

    // REQ-4 example: Missing `ownershipType` -> stops with "ownershipType is required to create tn_project", nothing written.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task RunAsync_MissingOwnershipType_SaysSoAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml(includeOwnership: false)), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "REQ-4: a refused file should exit with 1.");
        Expect.Contains("ownershipType is required to create tn_project", console.Output, "REQ-4: the user should be told ownershipType is required.");
        Expect.Empty(client.CreateEntityCalls, "REQ-4: nothing should be written when the file is refused.");
    }

    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task RunAsync_MissingPrimaryNameAttribute_SaysSoAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml(includePrimaryName: false)), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "REQ-4: a refused file should exit with 1.");
        Expect.Contains("primaryNameAttribute is required to create tn_project", console.Output, "REQ-4: the user should be told primaryNameAttribute is required.");
        Expect.Empty(client.CreateEntityCalls, "REQ-4: nothing should be written when the file is refused.");
    }

    // REQ-5 example: `isActivity: true` -> "Activity tables aren't supported by table create yet".
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public async Task RunAsync_ActivityTable_SaysActivityTablesArentSupportedYet()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml(isActivity: true)), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "REQ-5: an activity table should be refused with exit code 1.");
        Expect.Contains("Activity tables aren't supported by table create yet", console.Output, "REQ-5: the user should be told activity tables aren't supported yet.");
        Expect.Empty(client.CreateEntityCalls, "REQ-5: nothing should be written for an activity table.");
    }

    // ---- REQ-6: already exists ----

    // REQ-6 example: Second run prints "tn_project already exists, nothing to create".
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task RunAsync_TableAlreadyExists_SaysSoAndChangesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true, solution: "TNCore"), CancellationToken.None);

        Expect.Equal(0, exitCode, "REQ-6: an existing table is not an error; the command should exit 0.");
        Expect.Contains("tn_project already exists, nothing to create", console.Output, "REQ-6: the user should be told the table already exists.");
        Expect.Empty(client.CreateEntityCalls, "REQ-6: nothing should be written for a table that already exists.");
    }

    // ---- REQ-7: shows the plan, asks before writing, --yes / --whatif as in the other imports ----

    // REQ-7 example: `--whatif` prints the planned table + primary column, writes nothing.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task RunAsync_WhatIf_PrintsThePlannedTableAndPrimaryColumnAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-7: --whatif should succeed. Output: <{console.Output}>");
        Expect.Contains("tn_project", console.Output, "REQ-7: --whatif should show the table that would be created.");
        Expect.Contains("tn_name", console.Output, "REQ-7: --whatif should show the primary name column that would be created.");
        Expect.Empty(client.CreateEntityCalls, "REQ-7: --whatif should not create anything.");
    }

    // REQ-7: "asks before writing" - answering no writes nothing.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task RunAsync_WithoutYes_DecliningTheConfirmationWritesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-7: declining the confirmation should exit 0, as the other imports do. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "REQ-7: answering no to the confirmation should not create the table.");
    }

    // REQ-7: answering yes creates it.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task RunAsync_WithoutYes_AcceptingTheConfirmationCreatesTheTable()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"REQ-7: confirming should create the table and exit 0. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateEntityCalls.Count, "REQ-7: answering yes to the confirmation should create the table once.");
    }

    // ---- REQ-11: the input file is never rewritten ----

    // REQ-11: "No stage rewrites local YAML files." For `table create` itself: the file keeps its exact bytes, created or not.
    [Theory]
    [Trait("Requirement", "REQ-11")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LeavesTheInputFileByteIdentical(bool whatIf)
    {
        using var console = new ConsoleHarness();
        var (command, _) = CreateCommand(tableExistsLive: false);
        var path = WriteTableFile(TableCreateFixtures.TableYaml());
        var before = File.ReadAllBytes(path);

        await command.RunAsync(Settings(path, yes: !whatIf, whatIf: whatIf), CancellationToken.None);

        Expect.True(before.SequenceEqual(File.ReadAllBytes(path)), $"REQ-11: tn_project.table.yml should be byte-identical after 'table create' (whatIf: {whatIf}).");
    }

    // ---- REQ-12: a rejected create names the table ----

    // REQ-12 example: Creation of `tn_project` rejected -> error names `tn_project`.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task RunAsync_DataverseRejectsTheCreate_ExitsWithAnErrorNamingTheTable()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);
        client.CreateEntityFailure = new HttpRequestException("Dataverse request failed (400 BadRequest): schema name is already in use");

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "REQ-12: a rejected create should exit with 1.");
        Expect.Contains("tn_project", console.Output, "REQ-12: the error should name the table that failed.");
        Expect.Contains("schema name is already in use", console.Output, "REQ-12: the error should include Dataverse's own reason.");
    }
}

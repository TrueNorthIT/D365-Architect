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
/// input file alone.
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

    // ---- the command exists ----

    // "A new `table create` command" - it is registered under the `table` branch with the options the requirements use.
    // Code path: TableCommands.Configure registering CreateTableCommand as "create".
    [Fact]
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

        Expect.Equal(0, exitCode, $"'table create --help' should succeed, which needs the command registered under 'table'. Output: <{console.Output}>");
        foreach (var option in new[] { "--input", "--solution", "--yes", "--whatif" })
        {
            Expect.Contains(option, console.Output, $"'table create' should offer {option}.");
        }
    }

    // Example: `table create --input tn_project.table.yml --solution TNCore` on an environment without `tn_project`: table exists afterwards.
    [Fact]
    public async Task RunAsync_TableMissing_YesCreatesItWithoutPrompting()
    {
        using var console = new ConsoleHarness(); // no answers: a prompt would fail the test
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"creating a missing table with --yes should succeed. Output: <{console.Output}>");
        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal("tn_Project", (string?)call.Body["SchemaName"], "the table from the file should be created.");
    }

    // ---- --solution reaches the create ----

    [Fact]
    public async Task RunAsync_WithSolution_CreatesTheTableInThatSolution()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true, solution: "TNCore"), CancellationToken.None);

        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal("TNCore", call.SolutionUniqueName, "'--solution TNCore' should create the table inside TNCore.");
    }

    // ---- refusals ----

    // Example: Missing `ownershipType` -> stops with "ownershipType is required to create tn_project", nothing written.
    [Fact]
    public async Task RunAsync_MissingOwnershipType_SaysSoAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml(includeOwnership: false)), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "a refused file should exit with 1.");
        Expect.Contains("ownershipType is required to create tn_project", console.Output, "the user should be told ownershipType is required.");
        Expect.Empty(client.CreateEntityCalls, "nothing should be written when the file is refused.");
    }

    [Fact]
    public async Task RunAsync_MissingPrimaryNameAttribute_SaysSoAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml(includePrimaryName: false)), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "a refused file should exit with 1.");
        Expect.Contains("primaryNameAttribute is required to create tn_project", console.Output, "the user should be told primaryNameAttribute is required.");
        Expect.Empty(client.CreateEntityCalls, "nothing should be written when the file is refused.");
    }

    // Example: `isActivity: true` -> "Activity tables aren't supported by table create yet".
    [Fact]
    public async Task RunAsync_ActivityTable_SaysActivityTablesArentSupportedYet()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml(isActivity: true)), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "an activity table should be refused with exit code 1.");
        Expect.Contains("Activity tables aren't supported by table create yet", console.Output, "the user should be told activity tables aren't supported yet.");
        Expect.Empty(client.CreateEntityCalls, "nothing should be written for an activity table.");
    }

    // ---- already exists ----

    // Example: Second run prints "tn_project already exists, nothing to create".
    [Fact]
    public async Task RunAsync_TableAlreadyExists_SaysSoAndChangesNothing()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: true);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true, solution: "TNCore"), CancellationToken.None);

        Expect.Equal(0, exitCode, "an existing table is not an error; the command should exit 0.");
        Expect.Contains("tn_project already exists, nothing to create", console.Output, "the user should be told the table already exists.");
        Expect.Empty(client.CreateEntityCalls, "nothing should be written for a table that already exists.");
    }

    // ---- shows the plan, asks before writing, --yes / --whatif as in the other imports ----

    // Example: `--whatif` prints the planned table + primary column, writes nothing.
    [Fact]
    public async Task RunAsync_WhatIf_PrintsThePlannedTableAndPrimaryColumnAndWritesNothing()
    {
        using var console = new ConsoleHarness(); // --whatif never prompts
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"--whatif should succeed. Output: <{console.Output}>");
        Expect.Contains("tn_project", console.Output, "--whatif should show the table that would be created.");
        Expect.Contains("tn_name", console.Output, "--whatif should show the primary name column that would be created.");
        Expect.Empty(client.CreateEntityCalls, "--whatif should not create anything.");
    }

    // "asks before writing" - answering no writes nothing.
    [Fact]
    public async Task RunAsync_WithoutYes_DecliningTheConfirmationWritesNothing()
    {
        using var console = new ConsoleHarness("n");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"declining the confirmation should exit 0, as the other imports do. Output: <{console.Output}>");
        Expect.Empty(client.CreateEntityCalls, "answering no to the confirmation should not create the table.");
    }

    // Answering yes creates it.
    [Fact]
    public async Task RunAsync_WithoutYes_AcceptingTheConfirmationCreatesTheTable()
    {
        using var console = new ConsoleHarness("y");
        var (command, client) = CreateCommand(tableExistsLive: false);

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml())), CancellationToken.None);

        Expect.Equal(0, exitCode, $"confirming should create the table and exit 0. Output: <{console.Output}>");
        Expect.Equal(1, client.CreateEntityCalls.Count, "answering yes to the confirmation should create the table once.");
    }

    // ---- the input file is never rewritten ----

    // "No stage rewrites local YAML files." For `table create` itself: the file keeps its exact bytes, created or not.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_LeavesTheInputFileByteIdentical(bool whatIf)
    {
        using var console = new ConsoleHarness();
        var (command, _) = CreateCommand(tableExistsLive: false);
        var path = WriteTableFile(TableCreateFixtures.TableYaml());
        var before = File.ReadAllBytes(path);

        await command.RunAsync(Settings(path, yes: !whatIf, whatIf: whatIf), CancellationToken.None);

        Expect.True(before.SequenceEqual(File.ReadAllBytes(path)), $"tn_project.table.yml should be byte-identical after 'table create' (whatIf: {whatIf}).");
    }

    // ---- a rejected create names the table ----

    // Example: Creation of `tn_project` rejected -> error names `tn_project`.
    [Fact]
    public async Task RunAsync_DataverseRejectsTheCreate_ExitsWithAnErrorNamingTheTable()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(tableExistsLive: false);
        client.CreateEntityFailure = new HttpRequestException("Dataverse request failed (400 BadRequest): schema name is already in use");

        var exitCode = await command.RunAsync(Settings(WriteTableFile(TableCreateFixtures.TableYaml()), yes: true), CancellationToken.None);

        Expect.Equal(1, exitCode, "a rejected create should exit with 1.");
        Expect.Contains("tn_project", console.Output, "the error should name the table that failed.");
        Expect.Contains("schema name is already in use", console.Output, "the error should include Dataverse's own reason.");
    }
}

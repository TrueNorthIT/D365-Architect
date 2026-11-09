using D365Architect.Commands;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands;

/// <summary>
/// The --help copy of the commands whose behaviour changed in 1.1.0 (the
/// solution import prompt flow) or whose feature only lived in the release
/// notes (the non-interactive sign-in from d365architect_AUTH_MODE /
/// d365architect_URL): the help text must describe the behaviour the user
/// actually gets, so a pipeline's sign-in is discoverable without the
/// release notes.
/// Driven through a real <see cref="CommandApp"/> configured by
/// <see cref="CommandAppConfiguration"/> (the production command tree, as
/// <c>Program.cs</c> uses it), like <see cref="PublishOptionTests"/>.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class HelpTextTests
{
    private static CommandApp BuildApp(ConsoleHarness console)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            CommandAppConfiguration.Configure(config);
        });
        return app;
    }

    // "'solution import' no longer confirms every file individually: it previews every file, prints only the
    // diffs of the changed ones, asks once ('Import N changes?') and asks a second time only to create tables
    // that don't exist yet."
    [Fact]
    public async Task SolutionImportHelp_DescribesTheSharedConfirmation()
    {
        using var console = new ConsoleHarness();

        var exitCode = await BuildApp(console).RunAsync(["solution", "import", "--help"]);

        Expect.Equal(0, exitCode, $"'solution import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("'Import N changes?'", console.Output,
            $"the help should say the import previews every file and asks once to import the changes. Output: <{console.Output}>");
        Expect.Contains("diffs of the files that changed", console.Output,
            $"the help should say only the changed files' diffs are printed. Output: <{console.Output}>");
        Expect.Contains("second time", console.Output,
            $"the help should say a second prompt only appears for tables that don't exist yet. Output: <{console.Output}>");
        Expect.True(!console.Output.Contains("each with its own diff and confirmation", StringComparison.Ordinal),
            $"'solution import --help' still describes the removed per-file confirmation flow. Output: <{console.Output}>");
    }

    // "'solution import --yes' skips the one or two confirmation prompts (the shared import question, and the
    // table-creation question when the folder has tables to create) — not a per-file prompt any more."
    [Fact]
    public async Task SolutionImportHelp_YesDescribesSkippingThePrompts()
    {
        using var console = new ConsoleHarness();

        var exitCode = await BuildApp(console).RunAsync(["solution", "import", "--help"]);

        Expect.Equal(0, exitCode, $"'solution import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("confirmation prompt(s)", console.Output,
            $"the --yes description should say it skips the confirmation prompt(s). Output: <{console.Output}>");
        Expect.Contains("table-creation question", console.Output,
            $"the --yes description should name the second prompt (the one that creates missing tables). Output: <{console.Output}>");
    }

    // "The non-interactive sign-in (d365architect_AUTH_MODE / d365architect_URL) is documented in 'auth --help',
    // with the five modes, so a pipeline sign-in is discoverable from the CLI itself."
    [Fact]
    public async Task AuthHelp_DocumentsTheEnvironmentVariableSignIn()
    {
        using var console = new ConsoleHarness();

        var exitCode = await BuildApp(console).RunAsync(["auth", "--help"]);

        Expect.Equal(0, exitCode, $"'auth --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("d365architect_AUTH_MODE", console.Output,
            $"the auth help should mention the d365architect_AUTH_MODE variable. Output: <{console.Output}>");
        Expect.Contains("d365architect_URL", console.Output,
            $"the auth help should mention the d365architect_URL variable. Output: <{console.Output}>");
        foreach (var mode in new[] { "clientsecret", "certificate", "azurecli", "managedidentity", "workloadidentity" })
        {
            Expect.Contains(mode, console.Output, $"the auth help should list the '{mode}' sign-in mode. Output: <{console.Output}>");
        }
    }

    // "'whoami --help' says whoami reports the non-interactive sign-in (and the d365architect_AUTH_MODE used)
    // when the sign-in came from environment variables."
    [Fact]
    public async Task WhoAmIHelp_SaysItReportsTheSignInMode()
    {
        using var console = new ConsoleHarness();

        var exitCode = await BuildApp(console).RunAsync(["whoami", "--help"]);

        Expect.Equal(0, exitCode, $"'whoami --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("d365architect_AUTH_MODE", console.Output,
            $"the whoami help should mention the d365architect_AUTH_MODE variable. Output: <{console.Output}>");
        Expect.Contains("non-interactive sign-in", console.Output,
            $"the whoami help should say it reports the non-interactive sign-in and its mode. Output: <{console.Output}>");
    }
}

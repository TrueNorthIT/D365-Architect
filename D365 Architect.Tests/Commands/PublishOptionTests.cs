using D365Architect.Commands;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands;

/// <summary>
/// The command-line surface of publishing (the "publish by default" behaviour,
/// tagged with the "Feature" trait "publish-by-default"): every command
/// that publishes offers <c>--no-publish</c>, and the old publish-only <c>--publish</c> option is gone.
/// Driven through a real <see cref="CommandApp"/> configured by <see cref="CommandAppConfiguration"/> (the
/// production command tree and parser settings, as <c>Program.cs</c> uses them), so it checks the
/// <c>[CommandOption]</c> attributes on the settings classes and that the parser rejects unknown options.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class PublishOptionTests
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

    public static TheoryData<string, string> TableCreateImportViewChoiceAndForm => new()
    {
        { "table", "create" },
        { "table", "import" },
        { "view", "import" },
        { "choice", "import" },
        { "form", "import" },
        { "solution", "import" },
    };

    // "`--no-publish` skips publishing on all four commands, and on `form import`" - and on `solution import --no-publish`:
    // each command's --help lists the option. Code path: [CommandOption("--no-publish")] on PublishableImportSettings.NoPublish and ImportSolutionCommand.Settings.NoPublish.
    [Theory]
    [Trait("Feature", "publish-by-default")]
    [MemberData(nameof(TableCreateImportViewChoiceAndForm))]
    public async Task Command_OffersANoPublishOption(string branch, string command)
    {
        using var console = new ConsoleHarness();

        var exitCode = await BuildApp(console).RunAsync([branch, command, "--help"]);

        Expect.Equal(0, exitCode, $"'{branch} {command} --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--no-publish", console.Output, $"'{branch} {command}' should offer --no-publish.");
    }

    public static TheoryData<string, string> TheFourPublishingImportsAndFormImport => new()
    {
        { "table", "create" },
        { "table", "import" },
        { "view", "import" },
        { "choice", "import" },
        { "form", "import" },
    };

    // "The old `--publish` option no longer exists (unknown-option error)." - parsing fails before the command runs, so it exits non-zero and says what it didn't recognise.
    // Covers the four commands that had it and `form import` (which the new --no-publish joins, so it must not inherit the old option either).
    // The settings are valid apart from --publish, so the only thing wrong is the option.
    [Theory]
    [Trait("Feature", "publish-by-default")]
    [MemberData(nameof(TheFourPublishingImportsAndFormImport))]
    public async Task Command_RejectsTheOldPublishOptionAsUnknown(string branch, string command)
    {
        using var console = new ConsoleHarness();
        int exitCode;
        try
        {
            exitCode = await BuildApp(console).RunAsync([branch, command, "--input", "anything.yml", "--publish"]);
        }
        catch (Exception ex)
        {
            Assert.Fail($"'--publish' was accepted by '{branch} {command}' - parsing went on to run the command, which then failed with {ex.GetType().Name}. It should be an unknown-option error.");
            return;
        }

        Expect.True(!console.Output.Contains("Could not resolve type", StringComparison.Ordinal),
            $"'--publish' was not rejected by the parser: it went on to build '{branch} {command}'. Spectre only fails the parse on an unknown option when the app's Settings.StrictParsing is on (CommandAppConfiguration). Output: <{console.Output}>");
        Expect.True(exitCode != 0, $"'{branch} {command} --publish' should fail with an unknown-option error. Output: <{console.Output}>");
        Expect.Contains("publish", console.Output, $"the error should name the option it didn't recognise. Output: <{console.Output}>");
    }
}

using System.Reflection;
using D365Architect.Commands;
using D365Architect.Commands.Choice;
using D365Architect.Commands.Form;
using D365Architect.Commands.Solution;
using D365Architect.Commands.Table;
using D365Architect.Commands.View;
using D365Architect.Services.Authentication;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands;

/// <summary>
/// The export commands and <c>whoami</c> hand-maintain a per-exception
/// catch list and (since the last error-handling sweep) each ends in a
/// final catch-all that routes *any* unanticipated exception through
/// <see cref="ErrorConsole"/> and exits 1 instead of crashing. These tests
/// drive one command with a fake service that throws something none of the
/// named catches recognise, and pin that it is reported rather than escaping
/// unhandled.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ExportCommandsErrorHandlingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ExportCommandsErrorHandlingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>An exception none of the commands' named catches recognise — the catch-all's job.</summary>
    private static Exception Unanticipated() => new InvalidOperationException("something unanticipated: AADSTS7000215");

    /// <summary>
    /// Runs one of the sealed commands' protected <c>ExecuteAsync</c> directly
    /// (they're sealed, so they can't be subclassed to expose it, and none of
    /// them have an import-style public <c>RunAsync</c>). The commands don't
    /// use the <c>CommandContext</c>, so a null context is fine.
    /// </summary>
    private static async Task<int> RunAsync<TSettings>(object command, TSettings settings, CancellationToken cancellationToken)
        where TSettings : CommandSettings
    {
        var method = command.GetType().GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var invoked = method.Invoke(command, [null, settings, cancellationToken])!;
        var task = (Task<int>)invoked;
        await task;
        return task.Result;
    }

    private static void ExpectReportedAndNonZero(int exitCode, string output, string what)
    {
        Expect.Equal(1, exitCode, $"{what} should exit non-zero when the export fails unanticipatedly. Output: <{output}>");
        Expect.Contains("InvalidOperationException", output, $"{what} should report the unanticipated failure's type. Output: <{output}>");
        Expect.Contains("something unanticipated: AADSTS7000215", output, $"{what} should report the unanticipated failure's message. Output: <{output}>");
    }

    // `whoami`'s named catch only covers authentication; an HttpRequestException from WhoAmIAsync (or a raw
    // failure from the full-name lookup) used to escape unhandled — it must be reported and exit 1.
    [Fact]
    public async Task WhoAmI_HttpFailure_IsReportedAndExitsNonZero()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient { WhoAmIFailure = new HttpRequestException("no route to host") };
        var command = new WhoAmICommand(new FakeAuthenticationService(), client);

        var exitCode = await RunAsync(command, new WhoAmICommand.Settings(), CancellationToken.None);

        Expect.Equal(1, exitCode, $"whoami should exit non-zero when WhoAmI fails. Output: <{console.Output}>");
        Expect.Contains("HttpRequestException", console.Output, "whoami should name the failure type. Output: <console.Output>");
        Expect.Contains("no route to host", console.Output, "whoami should show the failure message. Output: <console.Output>");
    }

    // Same shape, from TryGetUserFullNameAsync — the second call whoami makes after WhoAmIAsync.
    [Fact]
    public async Task WhoAmI_FullNameLookupFailure_IsReportedAndExitsNonZero()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient
        {
            WhoAmIResult = new WhoAmIResult(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            TryGetUserFullNameFailure = new InvalidOperationException("lookup blew up"),
        };
        var command = new WhoAmICommand(new FakeAuthenticationService(), client);

        var exitCode = await RunAsync(command, new WhoAmICommand.Settings(), CancellationToken.None);

        Expect.Equal(1, exitCode, $"whoami should exit non-zero when the full-name lookup fails. Output: <{console.Output}>");
        Expect.Contains("lookup blew up", console.Output, "whoami should show the failure message. Output: <console.Output>");
    }

    [Fact]
    public async Task ChoiceExport_UnanticipatedFailure_IsReportedAndExitsNonZero()
    {
        using var console = new ConsoleHarness();
        var command = new ExportChoiceCommand(new FakeAuthenticationService(), new GlobalChoiceExportFake(Unanticipated()));

        var exitCode = await RunAsync(command, new ExportChoiceCommand.Settings
        {
            Output = Path.Combine(_directory, "choices.yml"),
        }, CancellationToken.None);

        ExpectReportedAndNonZero(exitCode, console.Output, "choice export");
    }

    [Fact]
    public async Task TableExport_UnanticipatedFailure_IsReportedAndExitsNonZero()
    {
        using var console = new ConsoleHarness();
        var command = new ExportTableCommand(new FakeAuthenticationService(), new TableExportFake(Unanticipated()));

        var exitCode = await RunAsync(command, new ExportTableCommand.Settings
        {
            Table = "account",
            Output = Path.Combine(_directory, "account.table.yml"),
        }, CancellationToken.None);

        ExpectReportedAndNonZero(exitCode, console.Output, "table export");
    }

    [Fact]
    public async Task ViewExport_UnanticipatedFailure_IsReportedAndExitsNonZero()
    {
        using var console = new ConsoleHarness();
        var command = new ExportViewCommand(new FakeAuthenticationService(), new ViewExportFake(Unanticipated()));

        var exitCode = await RunAsync(command, new ExportViewCommand.Settings
        {
            Table = "account",
            Output = _directory,
        }, CancellationToken.None);

        ExpectReportedAndNonZero(exitCode, console.Output, "view export");
    }

    [Fact]
    public async Task FormExport_UnanticipatedFailure_IsReportedAndExitsNonZero()
    {
        using var console = new ConsoleHarness();
        var command = new ExportFormCommand(new FakeAuthenticationService(), new FormExportFake(Unanticipated()));

        var exitCode = await RunAsync(command, new ExportFormCommand.Settings
        {
            Table = "account",
            FormId = Guid.NewGuid(),
            Output = _directory,
        }, CancellationToken.None);

        ExpectReportedAndNonZero(exitCode, console.Output, "form export");
    }

    [Fact]
    public async Task SolutionExport_UnanticipatedFailure_IsReportedAndExitsNonZero()
    {
        using var console = new ConsoleHarness();
        var command = new ExportSolutionCommand(new FakeAuthenticationService(), new SolutionExportFake(Unanticipated()));

        var exitCode = await RunAsync(command, new ExportSolutionCommand.Settings
        {
            Solution = "examplesolution",
            Output = _directory,
        }, CancellationToken.None);

        ExpectReportedAndNonZero(exitCode, console.Output, "solution export");
    }
}

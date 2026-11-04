using D365Architect.Commands;
using D365Architect.Infrastructure;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands;

/// <summary>
/// Every export command must route any unanticipated exception to
/// <see cref="ErrorConsole"/> (full detail, including inner exceptions, and a
/// non-zero exit code) instead of letting it escape as an unhandled crash.
/// Each export service is faked to throw a plain exception with an inner one;
/// the command runs through a real <see cref="CommandApp"/> configured like
/// <see cref="CommandAppConfiguration"/> wires it in production, with only
/// authentication and the export service faked. The assertion is the same for
/// all five: exit code 1 and both levels of detail printed.
/// In the "Console" collection because the tests swap <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ExportCommandCatchAllTests
{
    private static readonly Exception Unexpected = new InvalidOperationException("unexpected boom", new InvalidOperationException("inner boom detail"));

    [Fact]
    public async Task Choice_Export_UnexpectedServiceFailure_IsReportedAndReturns1()
    {
        using var console = new ConsoleHarness();

        var exitCode = await RunThroughApp(console, services => services.AddSingleton<IGlobalChoiceExportService>(new ThrowingGlobalChoiceExportService()), "choice", "export");

        Assert.Equal(1, exitCode);
        Assert.Contains("unexpected boom", console.Output);
        Assert.Contains("inner boom detail", console.Output);
    }

    [Fact]
    public async Task Form_Export_UnexpectedServiceFailure_IsReportedAndReturns1()
    {
        using var console = new ConsoleHarness();

        var exitCode = await RunThroughApp(console, services => services.AddSingleton<IFormExportService>(new ThrowingFormExportService()), "form", "export", "--table", "account");

        Assert.Equal(1, exitCode);
        Assert.Contains("unexpected boom", console.Output);
        Assert.Contains("inner boom detail", console.Output);
    }

    [Fact]
    public async Task Solution_Export_UnexpectedServiceFailure_IsReportedAndReturns1()
    {
        using var console = new ConsoleHarness();

        var exitCode = await RunThroughApp(console, services => services.AddSingleton<ISolutionExportService>(new ThrowingSolutionExportService()), "solution", "export", "--solution", "examplesolution");

        Assert.Equal(1, exitCode);
        Assert.Contains("unexpected boom", console.Output);
        Assert.Contains("inner boom detail", console.Output);
    }

    [Fact]
    public async Task Table_Export_UnexpectedServiceFailure_IsReportedAndReturns1()
    {
        using var console = new ConsoleHarness();

        var exitCode = await RunThroughApp(console, services => services.AddSingleton<ITableExportService>(new ThrowingTableExportService()), "table", "export", "--table", "account");

        Assert.Equal(1, exitCode);
        Assert.Contains("unexpected boom", console.Output);
        Assert.Contains("inner boom detail", console.Output);
    }

    [Fact]
    public async Task View_Export_UnexpectedServiceFailure_IsReportedAndReturns1()
    {
        using var console = new ConsoleHarness();

        var exitCode = await RunThroughApp(console, services => services.AddSingleton<IViewExportService>(new ThrowingViewExportService()), "view", "export", "--table", "account");

        Assert.Equal(1, exitCode);
        Assert.Contains("unexpected boom", console.Output);
        Assert.Contains("inner boom detail", console.Output);
    }

    /// <summary>
    /// Registers a minimal command tree (authentication + the single faked
    /// export service <paramref name="registerExportService"/>, like
    /// <see cref="FormCliHost"/>) and runs the export command through a real
    /// <see cref="CommandApp"/>, the way Program.cs does.
    /// </summary>
    private static async Task<int> RunThroughApp(ConsoleHarness console, Action<IServiceCollection> registerExportService, params string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService());
        registerExportService(services);

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            config.PropagateExceptions();
            CommandAppConfiguration.Configure(config);
        });

        return await app.RunAsync(args);
    }

    // ---- fakes: one per export service, all throwing the same unexpected failure ----

    private sealed class ThrowingGlobalChoiceExportService : IGlobalChoiceExportService
    {
        public Task<string> ExportGlobalChoicesAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, CancellationToken cancellationToken) =>
            throw Unexpected;
    }

    private sealed class ThrowingFormExportService : IFormExportService
    {
        public Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken) =>
            throw Unexpected;

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) =>
            throw Unexpected;

        public ExportedForm ExportFormFromXml(string path, string entityLogicalName, string? type) =>
            throw Unexpected;
    }

    private sealed class ThrowingSolutionExportService : ISolutionExportService
    {
        public Task<SolutionExportResult> ExportSolutionAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken, IReadOnlyCollection<string>? tables = null) =>
            throw Unexpected;
    }

    private sealed class ThrowingTableExportService : ITableExportService
    {
        public Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) =>
            throw Unexpected;
    }

    private sealed class ThrowingViewExportService : IViewExportService
    {
        public Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) =>
            throw Unexpected;
    }
}

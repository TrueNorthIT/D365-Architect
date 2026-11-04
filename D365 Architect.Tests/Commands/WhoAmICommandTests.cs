using D365Architect.Commands;
using D365Architect.Infrastructure;
using D365Architect.Services.Authentication;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands;

/// <summary>
/// <c>whoami</c> failure paths: a transport failure from the WhoAmI request
/// (an <see cref="HttpRequestException"/>) and a failure in the full-name
/// lookup must be reported cleanly with a non-zero exit code, not escape as
/// an unhandled crash, and full exception detail (including inner exceptions)
/// must reach the console. The command runs through a real
/// <see cref="CommandApp"/> configured like <see cref="CommandAppConfiguration"/>
/// wires it in production, with only authentication and Dataverse faked.
/// In the "Console" collection because the tests swap <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class WhoAmICommandTests
{
    [Fact]
    public async Task RunAsync_WhoAmITransportFailure_PrintsFullDetailAndReturns1()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient { WhoAmIFailure = new HttpRequestException("inner transport detail") };

        var exitCode = await RunThroughApp(console, client);

        Assert.Equal(1, exitCode);
        Assert.Contains("HttpRequestException", console.Output);
        Assert.Contains("inner transport detail", console.Output);
    }

    [Fact]
    public async Task RunAsync_FullNameLookupFailure_PrintsFullDetailAndReturns1()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient
        {
            WhoAmI = new WhoAmIResult(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            TryGetUserFullNameFailure = new InvalidOperationException("inner lookup detail"),
        };

        var exitCode = await RunThroughApp(console, client);

        Assert.Equal(1, exitCode);
        Assert.Contains("inner lookup detail", console.Output);
    }

    private static async Task<int> RunThroughApp(ConsoleHarness console, IDataverseClient client)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService());
        services.AddSingleton<IDataverseClient>(client);

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            config.PropagateExceptions();
            CommandAppConfiguration.Configure(config);
        });

        return await app.RunAsync(["whoami"]);
    }
}

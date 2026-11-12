using D365Architect.Commands;
using D365Architect.Infrastructure;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Runs the real command tree (<see cref="CommandAppConfiguration"/>) for <c>security export</c>, <c>security import</c>,
/// <c>solution export</c>, <c>solution import</c> and <c>schema</c>, the way <c>Program.cs</c> wires them, with only Dataverse
/// (<see cref="Client"/>) and sign-in faked (and, for <c>solution export</c>, optionally the whole export service via <see cref="SolutionExport"/>).
/// Arguments are the command line as typed. Exceptions propagate, so a command that throws fails the test with that exception.
/// If a command gains a dependency, register it here as <c>Program.cs</c> does.
/// </summary>
internal sealed class SecurityCliHost
{
    public FakeDataverseClient Client { get; init; } = new();

    /// <summary>When set, registered as <see cref="ISolutionExportService"/> (what <c>solution export</c> asks); otherwise that service is not registered.</summary>
    public ISolutionExportService? SolutionExport { get; init; }

    public async Task<int> RunAsync(ConsoleHarness console, params string[] args)
    {
        var client = Client;
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService());
        services.AddSingleton<IDataverseClient>(client);
        services.AddSingleton<ImportRunner>();
        services.AddSingleton<ISecurityExportService, SecurityExportService>();
        services.AddSingleton<ISecurityImportService, SecurityImportService>();

        // What `solution import` needs to be built (as Program.cs registers them).
        services.AddSingleton<ITableCreateService>(new TableCreateService(client));
        services.AddSingleton<ITableImportService>(new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)));
        services.AddSingleton<IViewImportService>(new ViewImportService(client));
        services.AddSingleton<IFormImportService>(new FormImportService(client, new FormJsonDefinitionReader()));
        services.AddSingleton<IGlobalChoiceImportService>(new GlobalChoiceImportService(client));

        if (SolutionExport is not null)
        {
            services.AddSingleton(SolutionExport);
        }

        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            config.PropagateExceptions();
            CommandAppConfiguration.Configure(config);
        });

        return await app.RunAsync(args);
    }
}

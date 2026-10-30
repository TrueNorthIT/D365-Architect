using D365Architect.Commands;
using D365Architect.Infrastructure;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Runs the real command tree (<see cref="CommandAppConfiguration"/>) for the <c>form</c> commands, the way
/// <c>Program.cs</c> wires them, with only Dataverse (<see cref="Client"/>) and sign-in faked. Arguments are the
/// command line as typed (<c>"form", "export", "--from-xml", path</c>). Exceptions propagate, so a command that
/// throws fails the test with that exception. If a command gains a dependency, register it here as <c>Program.cs</c> does.
/// </summary>
internal sealed class FormCliHost
{
    private readonly IAuthenticationService _authentication;

    private FormCliHost(IAuthenticationService authentication) => _authentication = authentication;

    public FakeDataverseClient Client { get; } = new();

    /// <summary>Signed in, as the import tests need.</summary>
    public static FormCliHost SignedIn() => new(new FakeAuthenticationService());

    /// <summary>Not signed in, and any attempt to sign in fails the test: for the offline commands.</summary>
    public static (FormCliHost Host, NoSignInAuthentication Authentication) NotSignedIn()
    {
        var authentication = new NoSignInAuthentication();
        return (new FormCliHost(authentication), authentication);
    }

    public async Task<int> RunAsync(ConsoleHarness console, params string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_authentication);
        services.AddSingleton<IDataverseClient>(Client);
        services.AddSingleton<FormJsonDefinitionReader>();
        services.AddSingleton<IFormExportService, FormExportService>();
        services.AddSingleton<IFormXmlBuildService, FormXmlBuildService>();
        services.AddSingleton<IFormImportService, FormImportService>();
        services.AddSingleton<ImportRunner>();

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

/// <summary>Counts every sign-in attempt and fails it: a command that works on local files must never need one.</summary>
internal sealed class NoSignInAuthentication : IAuthenticationService
{
    public int Attempts { get; private set; }

    public Task<AuthenticatedContext> LoginInteractiveAsync(Uri environmentUrl, CancellationToken cancellationToken) => Fail();

    public Task<AuthenticatedContext> GetCurrentContextAsync(CancellationToken cancellationToken) => Fail();

    private Task<AuthenticatedContext> Fail()
    {
        Attempts++;
        throw new InvalidOperationException("The command tried to sign in, but it should work from local files without signing in.");
    }
}

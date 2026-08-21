using Declarative_D365.Commands;
using Declarative_D365.Commands.Auth;
using Declarative_D365.Commands.Environments;
using Declarative_D365.Infrastructure;
using Declarative_D365.Services;
using Declarative_D365.Services.Authentication;
using Declarative_D365.Services.Dataverse;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

// 1. Register every service the app's commands can depend on. This is the
//    single place new dependencies get added — commands never construct
//    their own services or reach for a static/singleton instance.
var services = new ServiceCollection();
services.AddSingleton<IEnvironmentService, EnvironmentService>();
services.AddSingleton(AuthenticationOptions.FromEnvironment());
services.AddSingleton<IAuthenticationService, MsalAuthenticationService>();
services.AddHttpClient<IDataverseClient, DataverseClient>();

// 2. Hand that container to Spectre.Console.Cli via the TypeRegistrar/
//    TypeResolver adapter, so every command is itself resolved through DI
//    (constructor injection), rather than newed up directly.
var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);

// 3. Wire up the top-level commands/branches. Each one owns its own
//    registration (name, description, sub-commands, examples) via its own
//    Configure(IConfigurator) method — this list is just the index of
//    what's plugged in. Add a new command/branch by adding one line here.
app.Configure(config =>
{
    config.SetApplicationName("d365cli");

    AuthCommands.Configure(config);
    WhoAmICommand.Configure(config);
    EnvironmentCommands.Configure(config);
});

return await app.RunAsync(args);

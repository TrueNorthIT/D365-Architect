using D365Architect.Commands;
using D365Architect.Commands.Auth;
using D365Architect.Commands.Environments;
using D365Architect.Commands.Form;
using D365Architect.Commands.Schema;
using D365Architect.Commands.Table;
using D365Architect.Commands.View;
using D365Architect.Infrastructure;
using D365Architect.Services;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
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

// Entity definition reader strategies: one per source format (XML for
// unpacked solution files, JSON for live Dataverse metadata). Each is
// wired directly into the one call site that needs it below, since that
// call site already knows its source format at compile time.
services.AddSingleton<EntityXmlDefinitionReader>();
services.AddSingleton<EntityJsonDefinitionReader>();

// Views and forms only ever need one strategy each — see ViewDefinition's
// doc comment for why the XML-vs-JSON split that entities need doesn't
// apply here.
services.AddSingleton<ViewJsonDefinitionReader>();
services.AddSingleton<FormJsonDefinitionReader>();

// Component-specific XML->YAML converters. Add one registration per
// component type (FormXml, SavedQuery, Ribbon, ...) as support grows;
// XmlToYamlConverterService picks whichever one recognises the file.
services.AddSingleton<IComponentXmlConverter, EntityXmlConverter>();
services.AddSingleton<IXmlToYamlConverterService, XmlToYamlConverterService>();

// Live export: pulls a table's metadata straight from Dataverse (JSON) and
// converts it with the same curated model/YAML output as the XML pipeline.
services.AddSingleton<ITableExportService, TableExportService>();
services.AddSingleton<IViewExportService, ViewExportService>();
services.AddSingleton<IFormExportService, FormExportService>();

// `form build-xml` reads the form's current live FormXML (when it already
// exists) so it can patch onto it instead of building one from scratch —
// see FormXmlWriter's own doc comment.
services.AddSingleton<IFormXmlBuildService, FormXmlBuildService>();

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
    config.SetApplicationName("d365architect");

    AuthCommands.Configure(config);
    WhoAmICommand.Configure(config);
    EnvironmentCommands.Configure(config);
    TableCommands.Configure(config);
    ViewCommands.Configure(config);
    FormCommands.Configure(config);
    SchemaCommands.Configure(config);
});

return await app.RunAsync(args);

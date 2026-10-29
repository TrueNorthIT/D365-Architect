using D365Architect.Commands.Auth;
using D365Architect.Commands.Choice;
using D365Architect.Commands.Environments;
using D365Architect.Commands.Form;
using D365Architect.Commands.Schema;
using D365Architect.Commands.Solution;
using D365Architect.Commands.Table;
using D365Architect.Commands.View;
using Spectre.Console.Cli;

namespace D365Architect.Commands;

/// <summary>
/// The command tree and parser settings of the app, lifted out of <c>Program.cs</c> so tests can drive the real
/// thing (the registrations, and the parser behaviour such as an unknown option being an error) through a
/// <see cref="CommandApp"/> of their own. <c>Program.cs</c> calls <see cref="Configure"/>.
/// STUB: the registrations are exactly what <c>Program.cs</c> had; <c>config.Settings.StrictParsing</c> is NOT set yet,
/// so Spectre lets an unrecognised option through instead of failing the parse (REQ-5 needs it to fail).
/// </summary>
public static class CommandAppConfiguration
{
    public static void Configure(IConfigurator config)
    {
        config.SetApplicationName("d365architect");

        AuthCommands.Configure(config);
        WhoAmICommand.Configure(config);
        EnvironmentCommands.Configure(config);
        TableCommands.Configure(config);
        ViewCommands.Configure(config);
        FormCommands.Configure(config);
        ChoiceCommands.Configure(config);
        SolutionCommands.Configure(config);
        SchemaCommands.Configure(config);
    }
}

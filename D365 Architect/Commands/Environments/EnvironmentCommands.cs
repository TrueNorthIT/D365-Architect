using Spectre.Console.Cli;

namespace D365Architect.Commands.Environments;

/// <summary>
/// Owns the `environment` branch: creates it and registers every
/// sub-command under it, colocated with the command classes themselves.
/// Called from Program.cs like any other top-level registration — to add
/// a new sub-command, drop a new Command/AsyncCommand class in this folder
/// and add one <c>AddCommand&lt;T&gt;</c> line below.
/// </summary>
internal static class EnvironmentCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("environment", branch =>
        {
            branch.SetDescription("Manage D365 environments.");

            branch.AddCommand<ListEnvironmentsCommand>("list")
                .WithDescription("Lists known D365 environments.");

            branch.AddCommand<SyncEnvironmentCommand>("sync")
                .WithDescription("Synchronises a D365 environment.")
                .WithExample("environment", "sync", "--environment", "dev");
        });
    }
}

using Spectre.Console.Cli;

namespace D365Architect.Commands.Auth;

/// <summary>
/// Owns the `auth` branch: creates it and registers every sub-command
/// under it, colocated with the command classes themselves. Called from
/// Program.cs like any other top-level registration — to add a new
/// sub-command (e.g. a future `logout`), drop a new Command/AsyncCommand
/// class in this folder and add one <c>AddCommand&lt;T&gt;</c> line below.
/// </summary>
internal static class AuthCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("auth", branch =>
        {
            branch.SetDescription("Sign in to a D365 environment. For a pipeline or other unattended run, skip 'auth login' and sign in from environment variables instead: set d365architect_AUTH_MODE (clientsecret, certificate, azurecli, managedidentity or workloadidentity) and d365architect_URL to the environment's URL; while d365architect_AUTH_MODE is set, any saved 'auth login' sign-in is ignored.");

            branch.AddCommand<LoginCommand>("login")
                .WithDescription("Interactively signs in to a D365 environment.")
                .WithExample("auth", "login", "--url", "https://yourorg.crm.dynamics.com");
        });
    }
}

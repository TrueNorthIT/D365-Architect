using Spectre.Console.Cli;

namespace D365Architect.Commands.View;

/// <summary>
/// Owns the `view` branch: creates it and registers every sub-command
/// under it, colocated with the command classes themselves. Called from
/// Program.cs like any other top-level registration.
/// </summary>
internal static class ViewCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("view", branch =>
        {
            branch.SetDescription("Work with D365 view (saved query) definitions.");

            branch.AddCommand<ExportViewCommand>("export")
                .WithDescription("Fetches every view defined on a table and saves each as its own YAML file.")
                .WithExample("view", "export", "--table", "account");
        });
    }
}

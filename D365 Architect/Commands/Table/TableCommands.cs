using Spectre.Console.Cli;

namespace D365Architect.Commands.Table;

/// <summary>
/// Owns the `table` branch: creates it and registers every sub-command
/// under it, colocated with the command classes themselves. Called from
/// Program.cs like any other top-level registration.
/// </summary>
internal static class TableCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("table", branch =>
        {
            branch.SetDescription("Work with D365 table definitions.");

            branch.AddCommand<ExportTableCommand>("export")
                .WithDescription("Fetches a table's live definition and saves it as YAML.")
                .WithExample("table", "export", "--table", "account");
        });
    }
}

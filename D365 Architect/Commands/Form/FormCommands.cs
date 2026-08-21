using Spectre.Console.Cli;

namespace D365Architect.Commands.Form;

/// <summary>
/// Owns the `form` branch: creates it and registers every sub-command
/// under it, colocated with the command classes themselves. Called from
/// Program.cs like any other top-level registration.
/// </summary>
internal static class FormCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("form", branch =>
        {
            branch.SetDescription("Work with D365 form definitions.");

            branch.AddCommand<ExportFormCommand>("export")
                .WithDescription("Fetches every form defined on a table and saves each as its own YAML file.")
                .WithExample("form", "export", "--table", "account");
        });
    }
}

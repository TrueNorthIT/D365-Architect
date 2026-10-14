using Spectre.Console.Cli;

namespace D365Architect.Commands.Schema;

/// <summary>
/// Owns the `schema` branch: creates it and registers every sub-command
/// under it, colocated with the command classes themselves. Called from
/// Program.cs like any other top-level registration.
/// </summary>
internal static class SchemaCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("schema", branch =>
        {
            branch.SetDescription("Work with this tool's YAML schema.");

            branch.AddCommand<ExportSchemaCommand>("export")
                .WithDescription("Writes the JSON Schema for a curated YAML shape (table, view, or form) to disk.")
                .WithExample("schema", "export", "--for", "table")
                .WithExample("schema", "export", "--for", "view")
                .WithExample("schema", "export", "--for", "form");

            branch.AddCommand<ConfigureVsCodeCommand>("configure-vscode")
                .WithDescription("Wires up VS Code YAML validation for *.table.yml/*.view.yml/*.form.yml in a folder.")
                .WithExample("schema", "configure-vscode")
                .WithExample("schema", "configure-vscode", "--pre-release");
        });
    }
}

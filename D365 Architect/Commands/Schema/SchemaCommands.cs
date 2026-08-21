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
                .WithDescription("Writes the JSON Schema for the table YAML shape to disk.")
                .WithExample("schema", "export", "--output", "schema/table.schema.json");
        });
    }
}

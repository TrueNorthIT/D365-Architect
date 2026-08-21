using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using D365Architect.Services.Schema;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Schema;

/// <summary>
/// `d365architect schema export [--output schema/table.schema.json]`
/// Writes the JSON Schema for the table YAML shape to disk, generated
/// straight from <see cref="EntityDefinitionSchemaGenerator"/> — no live
/// environment or sign-in needed, purely reflection over this tool's own model.
/// </summary>
public sealed class ExportSchemaCommand : Command<ExportSchemaCommand.Settings>
{
    // The default encoder escapes apostrophes/em-dashes/etc. as \uXXXX for
    // HTML safety, which is irrelevant for a schema file meant to be read
    // by developers — relax it so descriptions stay plain, readable text.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-o|--output <PATH>")]
        [Description("Path to write the JSON Schema to.")]
        public string Output { get; init; } = Path.Combine("schema", "table.schema.json");
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var schema = EntityDefinitionSchemaGenerator.Generate();

        var directory = Path.GetDirectoryName(settings.Output);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(settings.Output, schema.ToJsonString(JsonOptions));

        AnsiConsole.MarkupLine($"[green]Wrote[/] {settings.Output}");
        return 0;
    }
}

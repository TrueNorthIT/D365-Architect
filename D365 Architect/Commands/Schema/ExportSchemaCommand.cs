using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Schema;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Schema;

/// <summary>
/// `d365architect schema export [--for table|view] [--output schema/table.schema.json]`
/// Writes the JSON Schema for one of this tool's curated YAML shapes to
/// disk, generated straight from <see cref="YamlSchemaGenerator"/> — no live
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

    private sealed record AssetType(Type ModelType, string Title, string Description, string DefaultFileName);

    private static readonly IReadOnlyDictionary<string, AssetType> AssetTypes = new Dictionary<string, AssetType>(StringComparer.OrdinalIgnoreCase)
    {
        ["table"] = new AssetType(typeof(EntityDefinition), "D365 Architect table definition",
            "Declarative YAML shape for a Dynamics table, produced by `d365architect table export`.", "table.schema.json"),
        ["view"] = new AssetType(typeof(ViewDefinition), "D365 Architect view definition",
            "Declarative YAML shape for a Dynamics view, produced by `d365architect view export`.", "view.schema.json"),
    };

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-f|--for <ASSET_TYPE>")]
        [Description("Which asset type's YAML shape to generate a schema for: 'table' or 'view'.")]
        public string For { get; init; } = "table";

        [CommandOption("-o|--output <PATH>")]
        [Description("Path to write the JSON Schema to. Defaults to schema/<asset-type>.schema.json.")]
        public string? Output { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!AssetTypes.TryGetValue(settings.For, out var assetType))
        {
            AnsiConsole.MarkupLine($"[red]Unknown asset type '{settings.For}'.[/] Expected 'table' or 'view'.");
            return 1;
        }

        var schema = YamlSchemaGenerator.Generate(assetType.ModelType, assetType.Title, assetType.Description);

        var output = settings.Output ?? Path.Combine("schema", assetType.DefaultFileName);
        var directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(output, schema.ToJsonString(JsonOptions));

        AnsiConsole.MarkupLine($"[green]Wrote[/] {output}");
        return 0;
    }
}

using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Schema;

/// <summary>
/// `d365architect schema configure-vscode [--release|--pre-release] [--path DIR]`
/// Wires up VS Code's YAML validation for a folder by writing `yaml.schemas`
/// entries into its `.vscode/settings.json` — pointed at this repository's
/// raw GitHub schema files, never a local path. This has to work when
/// `d365architect.exe` is installed on the system PATH and run from some
/// project folder that's never had this repo cloned into it, so (unlike
/// `.vscode/settings.json` inside this repo's own checkout, which points at
/// local `./schema/*.schema.json` files sitting right there) there's no
/// local schema file this command could ever assume exists next to it.
/// </summary>
public sealed class ConfigureVsCodeCommand : Command<ConfigureVsCodeCommand.Settings>
{
    private const string RepositoryRawBaseUrl = "https://raw.githubusercontent.com/TrueNorthIT/D365-Architect";

    private static readonly (string GlobPattern, string FileName)[] SchemaFiles =
    [
        ("*.table.yml", "table.schema.json"),
        ("*.view.yml", "view.schema.json"),
        ("*.form.yml", "form.schema.json"),
    ];

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        // VS Code's own settings.json is JSONC (comments + trailing commas
        // allowed), not strict JSON — parsing has to tolerate that even
        // though writing back out below can't preserve it. See the
        // "comments aren't preserved" note printed after a successful merge.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--release")]
        [Description("Point at the latest stable release (the main branch's schemas). This is the default.")]
        public bool Release { get; init; }

        [CommandOption("--pre-release")]
        [Description("Point at the latest pre-release (the develop branch's schemas) instead of the stable release.")]
        public bool PreRelease { get; init; }

        [CommandOption("-p|--path <DIR>")]
        [Description("Folder to write .vscode/settings.json into. Defaults to the current directory.")]
        public string Path { get; init; } = Directory.GetCurrentDirectory();
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Release && settings.PreRelease)
        {
            AnsiConsole.MarkupLine("[red]Specify only one of --release or --pre-release, not both.[/]");
            return 1;
        }

        var branch = settings.PreRelease ? "develop" : "main";

        var vscodeDir = System.IO.Path.Combine(settings.Path, ".vscode");
        Directory.CreateDirectory(vscodeDir);
        var settingsPath = System.IO.Path.Combine(vscodeDir, "settings.json");

        var fileExisted = File.Exists(settingsPath);
        JsonObject root;
        if (fileExisted)
        {
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(File.ReadAllText(settingsPath), documentOptions: ParseOptions);
            }
            catch (JsonException ex)
            {
                ErrorConsole.Print($"{settingsPath} isn't valid JSON — fix or remove it first, then re-run this command: {ex.Message}");
                return 1;
            }

            if (parsed is not JsonObject existingRoot)
            {
                ErrorConsole.Print($"{settingsPath}'s top level isn't a JSON object — refusing to overwrite it.");
                return 1;
            }

            root = existingRoot;
        }
        else
        {
            root = new JsonObject();
        }

        if (root["yaml.schemas"] is not JsonObject yamlSchemas)
        {
            yamlSchemas = new JsonObject();
            root["yaml.schemas"] = yamlSchemas;
        }

        // Clear out any existing mapping — under some other URL, or a local
        // "./schema/..." path from a repo checkout — that already owns one
        // of our three glob patterns, so switching --release/--pre-release
        // (or re-running this after copying settings.json out of the repo
        // itself) doesn't leave a stale, conflicting duplicate behind. Only
        // handles the plain-string form of a yaml.schemas value; the
        // less common one-URL-to-many-globs array form is left untouched.
        var ownedGlobs = SchemaFiles.Select(s => s.GlobPattern).ToHashSet();
        foreach (var existingKey in yamlSchemas.Select(kvp => kvp.Key).ToList())
        {
            if (yamlSchemas[existingKey] is JsonValue value
                && value.TryGetValue<string>(out var glob)
                && ownedGlobs.Contains(glob))
            {
                yamlSchemas.Remove(existingKey);
            }
        }

        foreach (var (globPattern, fileName) in SchemaFiles)
        {
            yamlSchemas[$"{RepositoryRawBaseUrl}/{branch}/schema/{fileName}"] = JsonValue.Create(globPattern);
        }

        File.WriteAllText(settingsPath, root.ToJsonString(WriteOptions));

        AnsiConsole.MarkupLine($"[green]Wrote[/] {settingsPath} — *.table.yml/*.view.yml/*.form.yml now validate against the [bold]{branch}[/] branch's schemas.");
        if (fileExisted)
        {
            AnsiConsole.MarkupLine("[grey]Note: any comments in the existing settings.json were not preserved.[/]");
        }

        return 0;
    }
}

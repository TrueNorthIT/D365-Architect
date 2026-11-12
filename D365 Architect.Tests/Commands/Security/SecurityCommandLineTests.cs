using System.Text.Json.Nodes;
using D365Architect.Commands;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;

namespace D365Architect.Tests.Commands.Security;

/// <summary>
/// Issue #21 (security role privileges as YAML). The command-line surface, driven through a real <see cref="CommandApp"/> configured by
/// <see cref="CommandAppConfiguration"/> (as <c>Program.cs</c> uses it), the same way <c>PublishOptionTests</c> does:
/// REQ-6/19 the options of <c>security export</c> and REQ-7/9/12/19 of <c>security import</c> are offered (and repeatable where the requirement
/// says so), and REQ-21 <c>schema export --for security|role</c> and <c>schema configure-vscode</c> cover the two new file types.
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityCommandLineTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static CommandApp BuildApp(ConsoleHarness console)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            CommandAppConfiguration.Configure(config);
        });
        return app;
    }

    public static TheoryData<string, string, string> OptionsOffered => new()
    {
        { "export", "--solution", "REQ-6: security export --solution x names the solution to export." },
        { "export", "--output", "REQ-6: security export writes under --output, as solution export does." },
        { "export", "--table", "REQ-6: security export --table limits to those tables' security files." },
        { "export", "--role", "REQ-19: security export --role limits to those role files." },
        { "import", "--input", "REQ-7: security import --input takes a solution folder or a single file." },
        { "import", "--solution", "REQ-18: security import --solution names the solution whose tables a role file leaves alone." },
        { "import", "--table", "REQ-7: security import --table limits to those tables." },
        { "import", "--role", "REQ-19: security import --role limits to those role files." },
        { "import", "--allow-removals", "REQ-9: security import --allow-removals lets a privilege missing from the file be removed." },
        { "import", "--whatif", "REQ-12: security import --whatif shows the diff and writes nothing." },
        { "import", "--yes", "REQ-7: security import --yes skips the prompt." },
    };

    // Code path: the [CommandOption] attributes on the security export/import Settings, registered by the security branch in CommandAppConfiguration.
    [Theory]
    [Trait("Requirement", "REQ-6")]
    [MemberData(nameof(OptionsOffered))]
    public async Task SecurityCommand_OffersTheOption(string command, string option, string because)
    {
        using var console = new ConsoleHarness();

        var exitCode = await BuildApp(console).RunAsync(["security", command, "--help"]);

        Expect.Equal(0, exitCode, $"'security {command} --help' should succeed. Output: <{console.Output}>");
        Expect.Contains(option, console.Output, $"{because} 'security {command}' should offer {option}.");
    }

    // REQ-21: "`schema export --for security` and `--for role` produce JSON schemas." Written to schema/<type>.schema.json by default; --output is used here.
    [Theory]
    [Trait("Requirement", "REQ-21")]
    [InlineData("security")]
    [InlineData("role")]
    public async Task SchemaExport_ForSecurityAndRole_WritesAJsonSchema(string assetType)
    {
        using var console = new ConsoleHarness();
        var output = _directory.File($"{assetType}.schema.json");

        var exitCode = await BuildApp(console).RunAsync(["schema", "export", "--for", assetType, "--output", output]);

        Expect.Equal(0, exitCode, $"REQ-21: 'schema export --for {assetType}' should succeed. Output: <{console.Output}>");
        Expect.True(File.Exists(output), $"REQ-21: the schema should be written to '{output}'.");
        var schema = JsonNode.Parse(File.ReadAllText(output))!;
        Expect.Equal("https://json-schema.org/draft/2020-12/schema", (string)schema["$schema"]!, "REQ-21: the file should be a JSON Schema.");
        Expect.Equal("object", (string)schema["type"]!, "REQ-21: both file types are YAML mappings at the top level.");
    }

    // REQ-21: the written security schema is the one that makes an editor flag `delete: Everyone`.
    [Fact]
    [Trait("Requirement", "REQ-21")]
    public async Task SchemaExport_ForSecurity_RestrictsDeleteToTheFiveAccessLevels()
    {
        using var console = new ConsoleHarness();
        var output = _directory.File("security.schema.json");

        var exitCode = await BuildApp(console).RunAsync(["schema", "export", "--for", "security", "--output", output]);

        Expect.Equal(0, exitCode, $"REQ-21: 'schema export --for security' should succeed. Output: <{console.Output}>");
        var deleteLevels = JsonNode.Parse(File.ReadAllText(output))!["properties"]!["roles"]!["items"]!["properties"]!["delete"]!["enum"]!.AsArray().Select(v => (string)v!).OrderBy(v => v, StringComparer.Ordinal).ToList();
        Expect.Sequence(["BusinessUnit", "None", "Organisation", "ParentChildBusinessUnit", "User"], deleteLevels, "REQ-21: 'delete' should allow only the five access levels, so 'delete: Everyone' is flagged invalid.");
    }

    // REQ-21: "`schema configure-vscode` wires them up."
    [Theory]
    [Trait("Requirement", "REQ-21")]
    [InlineData("*.security.yml", "security.schema.json")]
    [InlineData("*.role.yml", "role.schema.json")]
    public async Task SchemaConfigureVsCode_MapsTheNewFileTypesToTheirSchemas(string glob, string schemaFile)
    {
        using var console = new ConsoleHarness();

        var exitCode = await BuildApp(console).RunAsync(["schema", "configure-vscode", "--path", _directory.Path]);

        Expect.Equal(0, exitCode, $"REQ-21: schema configure-vscode should succeed. Output: <{console.Output}>");
        var yamlSchemas = JsonNode.Parse(File.ReadAllText(Path.Combine(_directory.Path, ".vscode", "settings.json")))!["yaml.schemas"]!.AsObject();
        var mapped = yamlSchemas.Where(kvp => (string?)kvp.Value == glob).Select(kvp => kvp.Key).ToList();
        Expect.True(mapped.Count == 1 && mapped[0].EndsWith($"/schema/{schemaFile}", StringComparison.Ordinal),
            $"REQ-21: {glob} files should be validated against schema/{schemaFile}. Entries for {glob}: <{string.Join(", ", mapped)}>.");
    }
}

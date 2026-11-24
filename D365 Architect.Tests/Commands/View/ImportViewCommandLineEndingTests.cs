using D365Architect.Commands;
using D365Architect.Commands.View;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.View;

/// <summary>
/// <c>view import</c> against a live view whose stored XML uses CRLF line
/// endings, and against one whose XML differs from the file only in
/// whitespace. YAML turns every line break into LF when a file is read, so a
/// view exported from Dataverse with a CRLF in its FetchXML or LayoutXML comes
/// back with LF; that alone mustn't count as a change. A difference that's
/// only whitespace still counts (the file is written as it is), but the plan
/// has to say so, since the pretty-printed diff has no lines to show.
/// Tagged with the "Feature" trait "view-line-endings".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "view-line-endings")]
public sealed class ImportViewCommandLineEndingTests : IDisposable
{
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");

    private const string ViewName = "Maintenance";
    private const string FetchXml = "<fetch version=\"1.0\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>";
    private const string LayoutXml = "<grid name=\"resultset\" object=\"1\" jump=\"name\"><row name=\"result\" id=\"accountid\"><cell name=\"name\" width=\"150\" /></row></grid>";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportViewCommandLineEndingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static (ImportViewCommand Command, FakeDataverseClient Client) CreateCommand(string liveFetchXml, string liveLayoutXml)
    {
        var client = new FakeDataverseClient();
        client.SavedQueriesById[ViewId] = new ExistingSavedQuery(ViewId, null, liveFetchXml, liveLayoutXml, "account", ViewName);
        return (new ImportViewCommand(new ViewImportService(client), new ImportRunner(new FakeAuthenticationService())), client);
    }

    /// <summary>Writes the file the way <c>view export</c> does, so it's read back through YAML like a real export.</summary>
    private string WriteFile(string fetchXml, string layoutXml)
    {
        var path = Path.Combine(_directory, "maintenance.view.yml");
        File.WriteAllText(path, ViewYamlSerializer.ToYaml(new ViewDefinition
        {
            Name = ViewName,
            Entity = "account",
            SavedQueryId = ViewId,
            FetchXml = fetchXml,
            LayoutXml = layoutXml,
        }));
        return path;
    }

    private static ImportViewCommand.Settings Settings(string input, bool yes = false, bool whatIf = false) =>
        new() { Input = input, Yes = yes, WhatIf = whatIf };

    [Fact]
    public async Task RunAsync_LiveLayoutXmlEndsWithCrlf_ReimportsAsNoChanges()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(FetchXml, LayoutXml + "\r\n");

        var exitCode = await command.RunAsync(Settings(WriteFile(FetchXml, LayoutXml + "\r\n"), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"An unedited re-import should succeed. Output: <{console.Output}>");
        Expect.Contains("No changes", console.Output, "A trailing CRLF in the live layout should not count as a change.");
        Assert.Empty(client.UpdateSavedQueryCalls);
    }

    [Fact]
    public async Task RunAsync_LiveFetchXmlHasCrlfInside_ReimportsAsNoChanges()
    {
        using var console = new ConsoleHarness();
        var liveFetchXml = "<fetch version=\"1.0\">\r\n  <entity name=\"account\">\r\n    <attribute name=\"name\" />\r\n  </entity>\r\n</fetch>";
        var (command, client) = CreateCommand(liveFetchXml, LayoutXml);

        var exitCode = await command.RunAsync(Settings(WriteFile(liveFetchXml, LayoutXml), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"An unedited re-import should succeed. Output: <{console.Output}>");
        Expect.Contains("No changes", console.Output, "CRLF line breaks inside the live FetchXML should not count as a change.");
        Assert.Empty(client.UpdateSavedQueryCalls);
    }

    [Fact]
    public async Task RunAsync_RealEditToAViewWithCrlfLayout_WritesTheFilesValues()
    {
        using var console = new ConsoleHarness();
        var (command, client) = CreateCommand(FetchXml, LayoutXml + "\r\n");
        var editedFetchXml = FetchXml.Replace("<attribute name=\"name\" />", "<attribute name=\"name\" /><attribute name=\"ownerid\" />");

        var exitCode = await command.RunAsync(Settings(WriteFile(editedFetchXml, LayoutXml + "\r\n"), yes: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"The import should succeed. Output: <{console.Output}>");
        var call = Assert.Single(client.UpdateSavedQueryCalls);
        Expect.Equal(editedFetchXml, call.FetchXml, "The edited FetchXML should be written as the file has it.");
    }

    [Fact]
    public async Task RunAsync_FetchXmlDiffersOnlyInWhitespace_SaysSoInsteadOfAnEmptyDiff()
    {
        using var console = new ConsoleHarness();
        var spacedFetchXml = FetchXml.Replace("<entity", " <entity");
        var (command, _) = CreateCommand(FetchXml, LayoutXml);

        var exitCode = await command.RunAsync(Settings(WriteFile(spacedFetchXml, LayoutXml), whatIf: true), CancellationToken.None);

        Expect.Equal(0, exitCode, $"--whatif should succeed. Output: <{console.Output}>");
        Expect.Contains("fetchxml: whitespace only", console.Output, "A whitespace-only difference should be named, not shown as an empty diff.");
    }
}

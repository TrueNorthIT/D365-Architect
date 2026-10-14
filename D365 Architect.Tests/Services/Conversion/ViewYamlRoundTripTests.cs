using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #35 (sub-issue of #30, "Solution import fixes"), a
/// <c>*.view.yml</c> can carry an optional top-level <c>savedQueryId</c>.
/// <c>view export</c> writes it, the YAML (de)serialisers round-trip it, and
/// it is omitted when absent (the strip-default-values convention in
/// docs/yaml-conventions.md; keys read as words, so camelCase
/// <c>savedQueryId</c>, not <c>savedqueryid</c>).
///
/// Written against <see cref="ViewJsonDefinitionReader"/>,
/// <see cref="ViewExportService"/>, <see cref="ViewYamlSerializer"/> and
/// <see cref="ViewYamlDeserializer"/>, using the issue's own real id
/// (<c>my-inspections-2.view.yml</c>'s <c>db985fe6-...</c>).
/// </summary>
public sealed class ViewYamlRoundTripTests
{
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid SiblingViewId = new("aaaaaaaa-0000-0000-0000-000000000001");

    private static string SavedQueriesJson(Guid first, Guid second) => $$"""
        {
          "value": [
            { "savedqueryid": "{{first}}", "name": "My Inspections", "returnedtypecode": "tn_inspection", "querytype": 0, "fetchxml": "<fetch/>", "layoutxml": "<grid/>" },
            { "savedqueryid": "{{second}}", "name": "My Inspections", "returnedtypecode": "tn_inspection", "querytype": 0, "fetchxml": "<fetch/>", "layoutxml": "<grid/>" }
          ]
        }
        """;

    // "`view export` writes it". Given two live views that share a
    // name and entity but have different savedqueryids, when
    // ViewJsonDefinitionReader.Read parses the Web API response, then each
    // ViewDefinition carries its own savedqueryid.
    [Fact]
    public void Read_SavedQueryIdInTheResponse_IsCapturedOnEachViewDefinition()
    {
        var views = new ViewJsonDefinitionReader().Read(SavedQueriesJson(ViewId, SiblingViewId));

        Assert.Equal([ViewId, SiblingViewId], views.Select(v => v.SavedQueryId));
    }

    // Through ViewExportService.ExportViewsAsync (what `view
    // export` runs), each exported file's YAML carries the top-level
    // `savedQueryId` key, and reading that YAML back gives the same id.
    [Fact]
    public async Task ExportViewsAsync_WritesEachViewsSavedQueryIdToItsYaml()
    {
        var client = new FakeDataverseClient { ViewDefinitionsJson = SavedQueriesJson(ViewId, SiblingViewId) };
        var service = new ViewExportService(client, new ViewJsonDefinitionReader());

        var exported = await service.ExportViewsAsync(new Uri("https://test.crm.dynamics.com"), "token", "tn_inspection", solutionUniqueName: null, CancellationToken.None);

        Assert.Equal(2, exported.Count);
        Assert.All(exported, file => Assert.True(
            file.Yaml.Split('\n').Any(line => line.StartsWith("savedQueryId:", StringComparison.Ordinal)),
            $"expected a top-level 'savedQueryId:' key in {file.FileNameStem}'s YAML, but it was:\n{file.Yaml}"));
        Assert.Equal([ViewId, SiblingViewId], exported.Select(file => ViewYamlDeserializer.FromYaml(file.Yaml).SavedQueryId));
    }

    // Round trip. Given a ViewDefinition with a SavedQueryId,
    // serialising and deserialising returns the same id.
    [Fact]
    public void ToYamlThenFromYaml_PreservesSavedQueryId()
    {
        var original = new ViewDefinition { Name = "My Inspections", Entity = "tn_inspection", SavedQueryId = ViewId, FetchXml = "<fetch/>" };

        var roundTripped = ViewYamlDeserializer.FromYaml(ViewYamlSerializer.ToYaml(original));

        Assert.Equal(ViewId, roundTripped.SavedQueryId);
    }

    // Hand-written YAML using the documented key is read as the id.
    [Fact]
    public void FromYaml_TopLevelSavedQueryIdKey_IsReadAsTheViewsId()
    {
        const string yaml = """
            view: My Inspections
            entity: tn_inspection
            savedQueryId: db985fe6-a889-f111-8077-002248426c39
            """;

        var view = ViewYamlDeserializer.FromYaml(yaml);

        Assert.Equal(ViewId, view.SavedQueryId);
    }

    // Omitted when absent (strip-default-values convention): a
    // view with no SavedQueryId serialises without the key at all - not as
    // `savedQueryId: null` or an empty guid.
    [Fact]
    public void ToYaml_NoSavedQueryId_OmitsTheKey()
    {
        var view = new ViewDefinition { Name = "My Inspections", Entity = "tn_inspection", FetchXml = "<fetch/>" };

        var yaml = ViewYamlSerializer.ToYaml(view);

        Assert.DoesNotContain("savedQueryId", yaml, StringComparison.OrdinalIgnoreCase);
    }

    // Backwards compatibility - a *.view.yml exported before the
    // field existed still deserialises, with SavedQueryId left null (so
    // import falls back to table + name).
    [Fact]
    public void FromYaml_NoSavedQueryIdKey_LeavesItNull()
    {
        const string yaml = """
            view: My Inspections
            entity: tn_inspection
            """;

        var view = ViewYamlDeserializer.FromYaml(yaml);

        Assert.Null(view.SavedQueryId);
    }
}

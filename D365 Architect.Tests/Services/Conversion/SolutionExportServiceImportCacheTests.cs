using D365Architect.Services.Caching;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers what <see cref="SolutionExportService"/> leaves in the <see cref="IImportCache"/>: for every view and form it exports, the
/// hash of the YAML it hands back (which is what the command writes) and the live <c>versionnumber</c>, so a later
/// <c>solution import</c> can skip a file nobody has edited. The export services themselves are fakes returning canned YAML.
/// Tagged with the "Feature" trait "skip-unchanged-assets".
/// </summary>
[Trait("Feature", "skip-unchanged-assets")]
public sealed class SolutionExportServiceImportCacheTests
{
    private const string Table = "tn_project";
    private static readonly Uri EnvironmentUrl = FakeAuthenticationService.EnvironmentUrl;
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid FormId = new("2b0c3c0e-6f1a-4c0e-9a51-6d3f0c7a1e22");

    private sealed class FakeTableExport : ITableExportService
    {
        public Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) =>
            Task.FromResult($"logicalName: {entityLogicalName}");
    }

    private sealed class FakeViewExport : IViewExportService
    {
        public IReadOnlyList<ExportedView> Views { get; set; } = [];

        /// <summary>Runs while the views are being read, as a change made in the maker portal mid-export would.</summary>
        public Action? WhileExporting { get; set; }

        public Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
        {
            WhileExporting?.Invoke();
            return Task.FromResult(Views);
        }
    }

    private sealed class FakeFormExport : IFormExportService
    {
        public IReadOnlyList<ExportedForm> Forms { get; set; } = [];

        public Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken) =>
            Task.FromResult(Forms);

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) => throw new NotImplementedException();

        public ExportedForm ExportFormFromXml(string path, string entityLogicalName, string? type) => throw new NotImplementedException();
    }

    private sealed class NoChoiceExport : IGlobalChoiceExportService
    {
        public Task<string> ExportGlobalChoicesAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, CancellationToken cancellationToken) =>
            Task.FromResult(GlobalChoiceYamlSerializer.ToYaml([]));
    }

    private sealed class Harness
    {
        public FakeDataverseClient Dataverse { get; }
        public FakeViewExport ViewExport { get; } = new();
        public FakeFormExport FormExport { get; } = new();
        public FakeImportCache Cache { get; } = new();
        public SolutionExportService Service { get; }

        public Harness(params string[] tables)
        {
            Dataverse = new FakeDataverseClient { SolutionEntityLogicalNames = _ => tables };
            Service = new SolutionExportService(Dataverse, new FakeTableExport(), ViewExport, FormExport, new NoChoiceExport(), new FakeSecurityExportService(), Cache);
        }

        public Task ExportAsync() => Service.ExportSolutionAsync(EnvironmentUrl, "token", "TNCore", CancellationToken.None);
    }

    private static string ViewYaml(string name = "All projects", Guid? id = null, string table = Table) =>
        ViewYamlSerializer.ToYaml(ViewOf(name: name, entity: table, savedQueryId: id ?? ViewId));

    private static string FormYaml(string name = "Project", Guid? id = null, string table = Table) =>
        FormYamlSerializer.ToYaml(FormOf(name, entity: table, formId: id ?? FormId));

    // REQ-7: "A view ... whose file hasn't been edited since the last export ... is counted as unchanged": the export is what leaves the first entry.
    // Given a solution with one view on tn_project at versionnumber 100, when it is exported, then the cache holds the view under its savedQueryId
    // with the hash of the YAML returned and version 100. Code path: SolutionExportService.ExportSolutionAsync -> IImportCache.Record.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task ExportSolutionAsync_View_RecordsTheHashOfItsYamlAndItsLiveVersion()
    {
        var harness = new Harness(Table);
        var yaml = ViewYaml();
        harness.ViewExport.Views = [new ExportedView("All projects", yaml)];
        harness.Dataverse.SavedQueryVersions[ViewId] = (Table, 100);

        await harness.ExportAsync();

        var entry = harness.Cache.Find(EnvironmentUrl, ImportAssetKind.View, ViewId);
        Expect.Equal(new ImportCacheEntry(ImportAssetKind.View, ViewId, ImportCacheFixtures.Sha256Hex(yaml), 100), entry, "REQ-7: the exported view should be recorded with the hash of its YAML and its live version number.");
    }

    // As above for a form, which is keyed by its formId and versioned from systemforms.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task ExportSolutionAsync_Form_RecordsTheHashOfItsYamlAndItsLiveVersion()
    {
        var harness = new Harness(Table);
        var yaml = FormYaml();
        harness.FormExport.Forms = [new ExportedForm("Project", yaml)];
        harness.Dataverse.SystemFormVersions[FormId] = (Table, 200);

        await harness.ExportAsync();

        var entry = harness.Cache.Find(EnvironmentUrl, ImportAssetKind.Form, FormId);
        Expect.Equal(new ImportCacheEntry(ImportAssetKind.Form, FormId, ImportCacheFixtures.Sha256Hex(yaml), 200), entry, "REQ-7: the exported form should be recorded with the hash of its YAML and its live version number.");
    }

    // REQ-7: the versions come from one query per table per kind.
    // Given two tables, each with views and forms, when exported, then each table's versions are asked for once per kind.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task ExportSolutionAsync_TwoTables_AsksForEachTablesViewAndFormVersionsOnce()
    {
        var harness = new Harness(Table, "tn_task");

        await harness.ExportAsync();

        Expect.Sequence([Table, "tn_task"], harness.Dataverse.SavedQueryVersionQueries, "REQ-7: each table's view versions should be read in one query.");
        Expect.Sequence([Table, "tn_task"], harness.Dataverse.SystemFormVersionQueries, "REQ-7: each table's form versions should be read in one query.");
    }

    // REQ-8: a change landing in Dataverse while the export is reading must not be baked in as already seen, or the next import would skip it.
    // Given the view is edited live (version 100 -> 101) while the export reads views, then the version recorded is 100, the one read before.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task ExportSolutionAsync_ViewChangesWhileExporting_RecordsTheVersionReadBeforeTheExport()
    {
        var harness = new Harness(Table);
        harness.ViewExport.Views = [new ExportedView("All projects", ViewYaml())];
        harness.Dataverse.SavedQueryVersions[ViewId] = (Table, 100);
        harness.ViewExport.WhileExporting = () => harness.Dataverse.SavedQueryVersions[ViewId] = (Table, 101);

        await harness.ExportAsync();

        Expect.Equal(100L, harness.Cache.Find(EnvironmentUrl, ImportAssetKind.View, ViewId)?.VersionNumber ?? -1, "REQ-8: the version should be read before the export reads the view, so an edit made meanwhile still looks like a change.");
    }

    // REQ-10: no evidence, no entry. A view whose live version isn't returned (e.g. deleted since the solution was read) is not recorded.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task ExportSolutionAsync_ViewWithNoLiveVersion_IsNotRecorded()
    {
        var harness = new Harness(Table);
        harness.ViewExport.Views = [new ExportedView("All projects", ViewYaml())];

        await harness.ExportAsync();

        Expect.True(harness.Cache.Find(EnvironmentUrl, ImportAssetKind.View, ViewId) is null, "REQ-10: a view with no live version has no evidence of matching, so it should not be recorded.");
    }

    // REQ-10: a view file without a savedQueryId can't be keyed, so it is left out rather than guessed.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task ExportSolutionAsync_ViewYamlWithoutAnId_IsNotRecorded()
    {
        var harness = new Harness(Table);
        harness.ViewExport.Views = [new ExportedView("All projects", ViewYamlSerializer.ToYaml(ViewOf(entity: Table)))];
        harness.Dataverse.SavedQueryVersions[ViewId] = (Table, 100);

        await harness.ExportAsync();

        Expect.Equal(0, harness.Cache.Entries.Count, "REQ-10: a view with no savedQueryId can't be matched to a live version, so nothing should be recorded for it.");
    }
}

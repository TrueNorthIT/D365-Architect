using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 (security role privileges as YAML). Covers how <see cref="SolutionExportService"/> composes <see cref="ISecurityExportService"/>
/// (a <see cref="FakeSecurityExportService"/>, whose own behaviour has its own tests in <c>SecurityExportServiceTests</c>) into the
/// solution export: each table's security file lands on that table's <see cref="SolutionExportEntity.SecurityYaml"/> and the role files
/// on <see cref="SolutionExportResult.Roles"/>. The other services are fakes that do nothing, since only the security hand-over is under test.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SolutionExportServiceSecurityTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    private sealed class NoTableExport : ITableExportService
    {
        public Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) =>
            Task.FromResult($"logicalName: {entityLogicalName}");
    }

    private sealed class NoViewExport : IViewExportService
    {
        public Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportedView>>([]);
    }

    private sealed class NoFormExport : IFormExportService
    {
        public Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExportedForm>>([]);

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) => throw new NotImplementedException();

        public ExportedForm ExportFormFromXml(string path, string entityLogicalName, string? type) => throw new NotImplementedException();
    }

    private sealed class NoChoiceExport : IGlobalChoiceExportService
    {
        public Task<string> ExportGlobalChoicesAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, CancellationToken cancellationToken) =>
            Task.FromResult(GlobalChoiceYamlSerializer.ToYaml([]));
    }

    private static (SolutionExportService Service, FakeSecurityExportService Security) CreateService(params string[] tables)
    {
        var security = new FakeSecurityExportService();
        var dataverse = new FakeDataverseClient { SolutionEntityLogicalNames = _ => tables };
        return (new SolutionExportService(dataverse, new NoTableExport(), new NoViewExport(), new NoFormExport(), new NoChoiceExport(), security), security);
    }

    // REQ-1: "`solution export` writes a security file into each table's folder..." and REQ-4: a table with no security file gets none.
    // Code path: SolutionExportService.ExportSolutionAsync -> SolutionExportEntity.SecurityYaml (null when the security export has no file for the table).
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task ExportSolutionAsync_GivesEachTableItsOwnSecurityFileAndNoneToATableWithoutOne()
    {
        var (service, security) = CreateService("tn_complaint", "tn_lookupcode");
        security.Result = new SecurityExportResult([new ExportedTableSecurity("tn_complaint", "table: tn_complaint\n")], []);

        var result = await service.ExportSolutionAsync(EnvironmentUrl, "token", "qad", CancellationToken.None);

        Expect.Equal("table: tn_complaint\n", result.Entities.Single(e => e.EntityLogicalName == "tn_complaint").SecurityYaml ?? "<null>", "REQ-1: tn_complaint's entry should carry its security file.");
        Expect.True(result.Entities.Single(e => e.EntityLogicalName == "tn_lookupcode").SecurityYaml is null, "REQ-4: tn_lookupcode has no security file, so its entry should carry none (never an empty one).");
    }

    // REQ-13: "`solution export` writes one file per solution role under `roles/` at the solution root."
    [Fact]
    [Trait("Requirement", "REQ-13")]
    public async Task ExportSolutionAsync_CarriesEveryRoleFileOnTheResult()
    {
        var (service, security) = CreateService("tn_complaint");
        security.Result = new SecurityExportResult([], [new ExportedRole("his-user", "HIS User", "name: HIS User\n"), new ExportedRole("his-super-user", "HIS Super User", "name: HIS Super User\n")]);

        var result = await service.ExportSolutionAsync(EnvironmentUrl, "token", "qad", CancellationToken.None);

        Expect.Sequence(["his-user", "his-super-user"], (result.Roles ?? []).Select(r => r.FileNameStem).ToList(), "REQ-13: the result should carry a role file for each role of the solution.");
    }

    // The security export is asked about the solution being exported, with no limit when the export is not limited.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task ExportSolutionAsync_AsksForTheSecurityOfTheSolutionBeingExported()
    {
        var (service, security) = CreateService("tn_complaint");

        await service.ExportSolutionAsync(EnvironmentUrl, "token", "qad", CancellationToken.None);

        var call = Assert.Single(security.Calls);
        Expect.Equal("qad", call.Solution, "REQ-1: security should be exported for the solution being exported.");
        Expect.True(call.Tables is null or { Count: 0 }, "REQ-1: an unlimited solution export should not limit the security export's tables.");
        Expect.True(call.Roles is null or { Count: 0 }, "REQ-13: an unlimited solution export should not limit the security export's roles.");
    }

    // `solution export --table x` exports only x's folder; its security file is x's own, and no role file is written
    // (the security export gives none when it is limited to tables - REQ-6/REQ-19).
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task ExportSolutionAsync_LimitedToATable_LimitsTheSecurityExportToThatTable()
    {
        var (service, security) = CreateService("tn_complaint", "tn_lookupcode");
        security.Result = new SecurityExportResult([new ExportedTableSecurity("tn_complaint", "table: tn_complaint\n")], []);

        var result = await service.ExportSolutionAsync(EnvironmentUrl, "token", "qad", CancellationToken.None, ["TN_COMPLAINT"]);

        var call = Assert.Single(security.Calls);
        Expect.Sequence(["tn_complaint"], (call.Tables ?? []).Select(t => t.ToLowerInvariant()).ToList(), "REQ-6: the security export should be limited to the table the solution export was limited to.");
        Expect.Equal("table: tn_complaint\n", Assert.Single(result.Entities).SecurityYaml ?? "<null>", "REQ-1: the limited export's one table should still carry its security file.");
        Expect.True(result.Roles is null or { Count: 0 }, "REQ-19: a table-limited export writes no role files.");
    }
}

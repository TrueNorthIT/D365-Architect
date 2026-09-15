using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="SolutionExportService"/>'s own orchestration logic —
/// discovering a solution's tables via <see cref="FakeDataverseClient"/>,
/// then fanning out across hand-written fakes of the four export services it
/// composes (<see cref="ITableExportService"/>/<see cref="IViewExportService"/>/
/// <see cref="IFormExportService"/>/<see cref="IGlobalChoiceExportService"/>) —
/// not any of those services' own conversion logic, which is already covered
/// by their own test suites.
/// </summary>
public sealed class SolutionExportServiceTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    private sealed class FakeTableExportService : ITableExportService
    {
        public List<(string EntityLogicalName, string? SolutionUniqueName)> Calls { get; } = [];

        public Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
        {
            Calls.Add((entityLogicalName, solutionUniqueName));
            return Task.FromResult($"logicalName: {entityLogicalName}");
        }
    }

    private sealed class FakeViewExportService : IViewExportService
    {
        public List<(string EntityLogicalName, string? SolutionUniqueName)> Calls { get; } = [];
        public IReadOnlyList<ExportedView> Views { get; set; } = [];

        public Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
        {
            Calls.Add((entityLogicalName, solutionUniqueName));
            return Task.FromResult(Views);
        }
    }

    private sealed class FakeFormExportService : IFormExportService
    {
        public List<(string EntityLogicalName, string? SolutionUniqueName, Guid? FormId)> Calls { get; } = [];
        public IReadOnlyList<ExportedForm> Forms { get; set; } = [];

        public Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken)
        {
            Calls.Add((entityLogicalName, solutionUniqueName, formId));
            return Task.FromResult(Forms);
        }

        public Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) =>
            throw new NotImplementedException("Solution export never needs the interactive-picker summary list — it always exports every form.");
    }

    private sealed class FakeGlobalChoiceExportService : IGlobalChoiceExportService
    {
        public string Yaml { get; set; } = GlobalChoiceYamlSerializer.ToYaml([]);

        public Task<string> ExportGlobalChoicesAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, CancellationToken cancellationToken) =>
            Task.FromResult(Yaml);
    }

    private static (SolutionExportService Service, FakeDataverseClient DataverseClient, FakeTableExportService TableExport, FakeViewExportService ViewExport, FakeFormExportService FormExport, FakeGlobalChoiceExportService ChoiceExport) CreateService(IReadOnlyList<string>? entityLogicalNames)
    {
        var dataverseClient = new FakeDataverseClient { SolutionEntityLogicalNames = _ => entityLogicalNames };
        var tableExport = new FakeTableExportService();
        var viewExport = new FakeViewExportService();
        var formExport = new FakeFormExportService();
        var choiceExport = new FakeGlobalChoiceExportService();
        var service = new SolutionExportService(dataverseClient, tableExport, viewExport, formExport, choiceExport);
        return (service, dataverseClient, tableExport, viewExport, formExport, choiceExport);
    }

    [Fact]
    public async Task ExportSolutionAsync_SolutionDoesNotExist_ThrowsSolutionNotFoundException()
    {
        var (service, _, _, _, _, _) = CreateService(entityLogicalNames: null);

        await Assert.ThrowsAsync<SolutionNotFoundException>(() =>
            service.ExportSolutionAsync(EnvironmentUrl, "token", "nonexistent", CancellationToken.None));
    }

    [Fact]
    public async Task ExportSolutionAsync_OneTable_ExportsItsTableViewsAndFormsWithSolutionScopeForwarded()
    {
        var (service, _, tableExport, viewExport, formExport, choiceExport) = CreateService(["account"]);
        viewExport.Views = [new ExportedView("active-accounts", "name: Active Accounts")];
        formExport.Forms = [new ExportedForm("account-main-form", "name: Account Main Form")];
        choiceExport.Yaml = GlobalChoiceYamlSerializer.ToYaml([]);

        var result = await service.ExportSolutionAsync(EnvironmentUrl, "token", "examplesolution", CancellationToken.None);

        var entity = Assert.Single(result.Entities);
        Assert.Equal("account", entity.EntityLogicalName);
        Assert.Equal("logicalName: account", entity.TableYaml);
        Assert.Equal("active-accounts", Assert.Single(entity.Views).FileNameStem);
        Assert.Equal("account-main-form", Assert.Single(entity.Forms).FileNameStem);

        // Every underlying export was scoped to the solution, and form
        // export was asked for every form on the table (formId: null), not
        // one chosen interactively.
        Assert.Equal(("account", "examplesolution"), Assert.Single(tableExport.Calls));
        Assert.Equal(("account", "examplesolution"), Assert.Single(viewExport.Calls));
        Assert.Equal(("account", "examplesolution", (Guid?)null), Assert.Single(formExport.Calls));
    }

    [Fact]
    public async Task ExportSolutionAsync_NoGlobalChoicesCustomized_ChoicesYamlIsNullRatherThanAnEmptyList()
    {
        var (service, _, _, _, _, choiceExport) = CreateService([]);
        choiceExport.Yaml = GlobalChoiceYamlSerializer.ToYaml([]);

        var result = await service.ExportSolutionAsync(EnvironmentUrl, "token", "examplesolution", CancellationToken.None);

        Assert.Null(result.ChoicesYaml);
    }

    [Fact]
    public async Task ExportSolutionAsync_SolutionCustomizesGlobalChoices_ChoicesYamlIsSet()
    {
        var (service, _, _, _, _, choiceExport) = CreateService([]);
        choiceExport.Yaml = GlobalChoiceYamlSerializer.ToYaml([new GlobalChoiceDefinition { Name = "tn_choice" }]);

        var result = await service.ExportSolutionAsync(EnvironmentUrl, "token", "examplesolution", CancellationToken.None);

        Assert.NotNull(result.ChoicesYaml);
        Assert.Contains("tn_choice", result.ChoicesYaml);
    }

    [Fact]
    public async Task ExportSolutionAsync_SolutionWithNoTableComponents_StillExportsChoicesWithNoEntities()
    {
        var (service, _, tableExport, _, _, choiceExport) = CreateService([]);
        choiceExport.Yaml = GlobalChoiceYamlSerializer.ToYaml([new GlobalChoiceDefinition { Name = "tn_choice" }]);

        var result = await service.ExportSolutionAsync(EnvironmentUrl, "token", "choicesonly", CancellationToken.None);

        Assert.Empty(result.Entities);
        Assert.Empty(tableExport.Calls);
        Assert.NotNull(result.ChoicesYaml);
    }
}

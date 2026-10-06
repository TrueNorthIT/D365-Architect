using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class SolutionExportService(
    IDataverseClient dataverseClient,
    ITableExportService tableExportService,
    IViewExportService viewExportService,
    IFormExportService formExportService,
    IGlobalChoiceExportService globalChoiceExportService) : ISolutionExportService
{
    public async Task<SolutionExportResult> ExportSolutionAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken)
    {
        var entityLogicalNames = await dataverseClient.TryGetSolutionEntityLogicalNamesAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
            ?? throw new SolutionNotFoundException(solutionUniqueName);

        var entities = new List<SolutionExportEntity>();
        foreach (var entityLogicalName in entityLogicalNames)
        {
            var tableYaml = await tableExportService.ExportTableAsync(environmentUrl, accessToken, entityLogicalName, solutionUniqueName, cancellationToken);
            var views = await viewExportService.ExportViewsAsync(environmentUrl, accessToken, entityLogicalName, solutionUniqueName, cancellationToken);

            // formId: null — every form this solution customizes on the
            // table, not one chosen interactively the way `form export`'s
            // own command layer narrows it down to a single pick.
            var forms = await formExportService.ExportFormsAsync(environmentUrl, accessToken, entityLogicalName, solutionUniqueName, formId: null, cancellationToken);

            entities.Add(new SolutionExportEntity(entityLogicalName, tableYaml, views, forms));
        }

        var choicesYaml = await globalChoiceExportService.ExportGlobalChoicesAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken);

        // A solution that customizes no global choices still gets a valid
        // (empty-list) YAML string back from the call above — parsed here
        // just to tell the two apart, so the caller writes no choices.yml
        // at all rather than one containing just "[]".
        var hasChoices = GlobalChoiceYamlDeserializer.FromYaml(choicesYaml).Count > 0;

        return new SolutionExportResult(entities, hasChoices ? choicesYaml : null);
    }
}

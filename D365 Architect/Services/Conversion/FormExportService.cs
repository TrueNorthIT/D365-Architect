using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class FormExportService(IDataverseClient dataverseClient, FormJsonDefinitionReader reader) : IFormExportService
{
    public async Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedFormIds = null;
        if (solutionUniqueName is not null)
        {
            allowedFormIds = await dataverseClient.TryGetSolutionSystemFormIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);
        }

        var json = await dataverseClient.GetFormDefinitionsJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        var forms = reader.Read(json, allowedFormIds);

        var usedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return forms
            .Select(form => new ExportedForm(AssetFileNaming.MakeUnique(AssetFileNaming.Slugify(form.Name), usedStems), FormYamlSerializer.ToYaml(form)))
            .ToList();
    }
}

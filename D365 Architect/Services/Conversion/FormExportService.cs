using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class FormExportService(IDataverseClient dataverseClient, FormJsonDefinitionReader reader) : IFormExportService
{
    public async Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken)
    {
        var allowedFormIds = await ResolveAllowedFormIdsAsync(environmentUrl, accessToken, solutionUniqueName, formId, cancellationToken);

        var json = await dataverseClient.GetFormDefinitionsJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        var forms = reader.Read(json, allowedFormIds);

        var usedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return forms
            .Select(form => new ExportedForm(AssetFileNaming.MakeUnique(AssetFileNaming.Slugify(form.Name), usedStems), FormYamlSerializer.ToYaml(form)))
            .ToList();
    }

    public async Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedFormIds = null;
        if (solutionUniqueName is not null)
        {
            allowedFormIds = await dataverseClient.TryGetSolutionSystemFormIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);
        }

        var json = await dataverseClient.GetFormSummariesJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        return reader.ReadSummaries(json, allowedFormIds);
    }

    private async Task<IReadOnlySet<Guid>?> ResolveAllowedFormIdsAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedFormIds = null;
        if (solutionUniqueName is not null)
        {
            allowedFormIds = await dataverseClient.TryGetSolutionSystemFormIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);
        }

        if (formId is null)
        {
            return allowedFormIds;
        }

        // A form id outside the solution's own forms (when both are given)
        // should export nothing, not silently fall back to the solution's
        // full set — this narrows to exactly the one id either way.
        return allowedFormIds is null || allowedFormIds.Contains(formId.Value)
            ? new HashSet<Guid> { formId.Value }
            : new HashSet<Guid>();
    }
}

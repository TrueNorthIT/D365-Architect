using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class ViewExportService(IDataverseClient dataverseClient, ViewJsonDefinitionReader reader) : IViewExportService
{
    public async Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedSavedQueryIds = null;
        if (solutionUniqueName is not null)
        {
            allowedSavedQueryIds = await dataverseClient.TryGetSolutionSavedQueryIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);
        }

        var json = await dataverseClient.GetViewDefinitionsJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        var views = reader.Read(json, allowedSavedQueryIds);

        var usedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return views
            .Select(view => new ExportedView(AssetFileNaming.MakeUnique(AssetFileNaming.Slugify(view.Name), usedStems), ViewYamlSerializer.ToYaml(view)))
            .ToList();
    }
}

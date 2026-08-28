using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class ViewImportService(IDataverseClient dataverseClient) : IViewImportService
{
    public async Task<ViewImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, ViewDefinition view, CancellationToken cancellationToken)
    {
        var existing = await dataverseClient.TryGetSavedQueryAsync(environmentUrl, accessToken, view.Entity, view.Name, cancellationToken)
            ?? throw new ViewNotFoundException(view.Entity, view.Name);

        return new ViewImportPreview(
            existing.SavedQueryId,
            existing.Description,
            view.Description,
            existing.FetchXml,
            view.FetchXml,
            existing.LayoutXml,
            view.LayoutXml);
    }

    public Task ApplyAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, CancellationToken cancellationToken) =>
        dataverseClient.UpdateSavedQueryAsync(
            environmentUrl,
            accessToken,
            preview.SavedQueryId,
            preview.NewDescription,
            preview.NewFetchXml,
            preview.NewLayoutXml,
            cancellationToken);
}

using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class ViewImportService(IDataverseClient dataverseClient) : IViewImportService
{
    public async Task<ViewImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, ViewDefinition view, CancellationToken cancellationToken)
    {
        // A savedQueryId in the YAML is authoritative: it disambiguates views
        // sharing a name, and a stale id is a clear error rather than a quiet
        // fall back to the name (which could update the wrong view).
        var existing = view.SavedQueryId is { } savedQueryId
            ? await dataverseClient.TryGetSavedQueryByIdAsync(environmentUrl, accessToken, savedQueryId, cancellationToken)
                ?? throw new ViewNotFoundException(view.Entity, view.Name, savedQueryId)
            : await dataverseClient.TryGetSavedQueryAsync(environmentUrl, accessToken, view.Entity, view.Name, cancellationToken)
                ?? throw new ViewNotFoundException(view.Entity, view.Name);

        // Only a by-id lookup carries the live table/name (the by-name lookup
        // already filtered on them). Same approach as form import's
        // BuildIdentityMismatchWarning, except a different table is refused
        // outright: it's an id pasted into the wrong file, and writing would
        // overwrite an unrelated view. A name-only difference is a warning,
        // since renaming a view is legitimate.
        if (existing.EntityLogicalName is not null
            && !string.Equals(existing.EntityLogicalName, view.Entity, StringComparison.OrdinalIgnoreCase))
        {
            throw new ViewIdentityMismatchException(existing.SavedQueryId, view.Entity, view.Name, existing.EntityLogicalName, existing.Name);
        }

        var identityMismatchWarning = existing.Name is not null && existing.Name != view.Name
            ? $"This YAML's savedQueryId resolves to the view '{existing.Name}' on '{existing.EntityLogicalName}', not this file's own '{view.Name}' — the id is still what's being used (it's authoritative, and the name itself isn't written), but double-check this is the file you meant."
            : null;

        return new ViewImportPreview(
            existing.SavedQueryId,
            existing.Description,
            view.Description,
            existing.FetchXml,
            view.FetchXml,
            existing.LayoutXml,
            view.LayoutXml)
        {
            IdentityMismatchWarning = identityMismatchWarning,
        };
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

    // Test-first stub (REQ-22, REQ-23): compile-only; the developer implements it.
    public Task ApplyAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, string? solutionUniqueName, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

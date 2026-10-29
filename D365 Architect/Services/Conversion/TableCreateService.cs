using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>
/// See <see cref="ITableCreateService"/>. Existence is checked before
/// anything else so a table that's already live is only ever reported as
/// existing - an older export with no <c>primaryNameAttribute</c> (or
/// <c>ownershipType</c>) must not start failing <c>solution import</c> for a
/// table it would never create anyway.
/// </summary>
public sealed class TableCreateService(IDataverseClient dataverseClient) : ITableCreateService
{
    public async Task<TableCreatePreview> PreviewAsync(Uri environmentUrl, string accessToken, EntityDefinition input, CancellationToken cancellationToken)
    {
        if (await dataverseClient.EntityExistsAsync(environmentUrl, accessToken, input.LogicalName, cancellationToken))
        {
            return new TableCreatePreview(input.LogicalName, AlreadyExists: true, RequestBody: null, PrimaryNameColumn: null, DeferredColumns: []);
        }

        var problems = TableCreateValidator.Validate(input);
        if (problems.Count > 0)
        {
            throw new TableCreateValidationException(problems);
        }

        var body = EntityMetadataJsonBuilder.BuildCreateBody(input);
        var deferred = input.Attributes.Select(a => a.Name).Where(name => name != input.PrimaryNameAttribute).ToList();
        return new TableCreatePreview(input.LogicalName, AlreadyExists: false, body, input.PrimaryNameAttribute, deferred);
    }

    public Task ApplyAsync(Uri environmentUrl, string accessToken, TableCreatePreview preview, CancellationToken cancellationToken) =>
        ApplyAsync(environmentUrl, accessToken, preview, solutionUniqueName: null, cancellationToken);

    public Task PublishAsync(Uri environmentUrl, string accessToken, TableCreatePreview preview, CancellationToken cancellationToken) =>
        dataverseClient.PublishEntityAsync(environmentUrl, accessToken, preview.EntityLogicalName, cancellationToken);

    public async Task ApplyAsync(Uri environmentUrl, string accessToken, TableCreatePreview preview, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        if (preview.AlreadyExists || preview.RequestBody is null)
        {
            return;
        }

        try
        {
            await dataverseClient.CreateEntityAsync(environmentUrl, accessToken, preview.RequestBody, solutionUniqueName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TableCreateFailedException(preview.EntityLogicalName, ex);
        }
    }
}

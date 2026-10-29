using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>Stub added by the test-first pass - see <see cref="ITableCreateService"/>.</summary>
public sealed class TableCreateService(IDataverseClient dataverseClient) : ITableCreateService
{
    public Task<TableCreatePreview> PreviewAsync(Uri environmentUrl, string accessToken, EntityDefinition input, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task ApplyAsync(Uri environmentUrl, string accessToken, TableCreatePreview preview, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task ApplyAsync(Uri environmentUrl, string accessToken, TableCreatePreview preview, string? solutionUniqueName, CancellationToken cancellationToken) => throw new NotImplementedException();
}

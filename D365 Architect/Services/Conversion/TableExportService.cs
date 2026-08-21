using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class TableExportService(IDataverseClient dataverseClient, EntityJsonDefinitionReader reader) : ITableExportService
{
    public async Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedAttributeMetadataIds = null;
        if (solutionUniqueName is not null)
        {
            allowedAttributeMetadataIds = await dataverseClient.TryGetSolutionAttributeMetadataIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);
        }

        var json = await dataverseClient.GetEntityDefinitionJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        var definition = reader.Read(json, allowedAttributeMetadataIds);
        return EntityYamlSerializer.ToYaml(definition);
    }
}

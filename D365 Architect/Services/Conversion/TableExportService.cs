using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class TableExportService(IDataverseClient dataverseClient, EntityJsonDefinitionReader reader, AttributeOptionSetFetcher optionSetFetcher) : ITableExportService
{
    public async Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedAttributeMetadataIds = null;
        if (solutionUniqueName is not null)
        {
            allowedAttributeMetadataIds = await dataverseClient.TryGetSolutionAttributeMetadataIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);

            // A solution that owns this table outright (the normal case for
            // a table created inside it) never gets individual Attribute
            // solutioncomponents at all — see
            // IDataverseClient.IsSolutionEntityIncludingSubcomponentsAsync's
            // own doc comment. Treat that as "no filter", not "the (empty)
            // explicit set is the real answer".
            if (await dataverseClient.IsSolutionEntityIncludingSubcomponentsAsync(environmentUrl, accessToken, solutionUniqueName, entityLogicalName, cancellationToken))
            {
                allowedAttributeMetadataIds = null;
            }
        }

        var json = await dataverseClient.GetEntityDefinitionJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        var optionSetJsonByAttribute = await optionSetFetcher.FetchAsync(environmentUrl, accessToken, entityLogicalName, json, cancellationToken);
        var definition = reader.Read(json, allowedAttributeMetadataIds, optionSetJsonByAttribute);
        return EntityYamlSerializer.ToYaml(definition);
    }
}

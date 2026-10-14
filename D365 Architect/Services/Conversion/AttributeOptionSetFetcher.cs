using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Fetches every option-bearing attribute's choice-value JSON ahead of an
/// <see cref="EntityJsonDefinitionReader.Read(string, IReadOnlySet{Guid}?, IReadOnlyDictionary{string, string}?)"/>
/// call — shared by <see cref="TableExportService"/> (the entity being
/// exported) and <see cref="TableImportService"/> (the existing live entity
/// re-read for the diff), so the same "scan the bulk JSON, then fetch each
/// option-bearing attribute in turn" approach isn't duplicated between them.
/// Sequential, one request per attribute, matching this codebase's existing
/// style elsewhere (no new parallel-request plumbing introduced for this).
/// </summary>
public sealed class AttributeOptionSetFetcher(IDataverseClient dataverseClient)
{
    public async Task<IReadOnlyDictionary<string, string>> FetchAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string entityDefinitionJson, CancellationToken cancellationToken)
    {
        var optionBearingAttributes = EntityJsonDefinitionReader.ListOptionBearingAttributes(entityDefinitionJson);
        if (optionBearingAttributes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var result = new Dictionary<string, string>();
        foreach (var (logicalName, type) in optionBearingAttributes)
        {
            result[logicalName] = await dataverseClient.GetAttributeOptionSetJsonAsync(environmentUrl, accessToken, entityLogicalName, logicalName, type, cancellationToken);
        }

        return result;
    }
}

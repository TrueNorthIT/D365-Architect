using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class GlobalChoiceExportService(IDataverseClient dataverseClient) : IGlobalChoiceExportService
{
    public async Task<string> ExportGlobalChoicesAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedMetadataIds = null;
        if (solutionUniqueName is not null)
        {
            allowedMetadataIds = await dataverseClient.TryGetSolutionOptionSetMetadataIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);
        }

        var json = await dataverseClient.GetGlobalOptionSetsJsonAsync(environmentUrl, accessToken, cancellationToken);
        var choices = GlobalChoiceJsonReader.ReadMany(json, allowedMetadataIds);

        // Sorted for a stable, readable file — the bulk query's own
        // ordering isn't documented, and re-exporting in a different order
        // every time would make every re-export look like a change even
        // when nothing actually did.
        var sorted = choices.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return GlobalChoiceYamlSerializer.ToYaml(sorted);
    }
}

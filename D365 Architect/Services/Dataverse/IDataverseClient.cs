namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thin wrapper over the Dataverse Web API. Takes an already-issued access
/// token per call rather than owning authentication itself — that's
/// <see cref="Authentication.IAuthenticationService"/>'s job.
/// </summary>
public interface IDataverseClient
{
    Task<WhoAmIResult> WhoAmIAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken);

    /// <summary>Best-effort lookup; returns null rather than throwing if it fails.</summary>
    Task<string?> TryGetUserFullNameAsync(Uri environmentUrl, string accessToken, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a table's metadata (including its columns) from the Web API's
    /// EntityDefinitions endpoint, as raw JSON — the shape
    /// <see cref="Conversion.EntityJsonDefinitionReader"/> reads.
    /// </summary>
    Task<string> GetEntityDefinitionJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a solution's unique name to the MetadataIds of the Attribute
    /// components it contains — i.e. which columns that solution actually
    /// customizes, as opposed to a table's full merged metadata. Returns
    /// null if no solution with that unique name exists.
    /// </summary>
    Task<IReadOnlySet<Guid>?> TryGetSolutionAttributeMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken);
}

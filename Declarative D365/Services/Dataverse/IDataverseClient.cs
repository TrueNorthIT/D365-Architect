namespace Declarative_D365.Services.Dataverse;

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
}

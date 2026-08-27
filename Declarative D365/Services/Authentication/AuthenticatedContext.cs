namespace Declarative_D365.Services.Authentication;

/// <summary>
/// A live, usable sign-in: which environment it's for, who's signed in,
/// and an access token ready to send to Dataverse.
/// </summary>
public sealed record AuthenticatedContext(Uri EnvironmentUrl, string Username, string AccessToken);

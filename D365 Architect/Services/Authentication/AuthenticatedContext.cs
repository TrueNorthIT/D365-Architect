namespace D365Architect.Services.Authentication;

/// <summary>
/// A live, usable sign-in: which environment it's for, who's signed in,
/// and an access token ready to send to Dataverse. <c>AuthMode</c> is set
/// only for a non-interactive sign-in from environment variables (see
/// <see cref="NonInteractiveAuthSettingsResolver"/>), and null for the
/// interactive `auth login` sign-in.
/// </summary>
public sealed record AuthenticatedContext(Uri EnvironmentUrl, string Username, string AccessToken, NonInteractiveAuthMode? AuthMode = null);

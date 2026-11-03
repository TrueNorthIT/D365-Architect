namespace D365Architect.Services.Authentication;

public interface IAuthenticationService
{
    /// <summary>
    /// Signs in interactively (opens the system browser) for the given
    /// D365 environment, and remembers the sign-in for later commands.
    /// </summary>
    Task<AuthenticatedContext> LoginInteractiveAsync(Uri environmentUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a usable context for whichever environment was last signed
    /// into, refreshing the access token silently. Throws
    /// <see cref="AuthenticationRequiredException"/> if there's no
    /// remembered sign-in, or it can no longer be refreshed silently.
    ///
    /// When d365architect_AUTH_MODE is set, signs in non-interactively from
    /// environment variables instead (see
    /// <see cref="NonInteractiveAuthSettingsResolver"/>), ignoring any
    /// remembered sign-in entirely.
    /// </summary>
    Task<AuthenticatedContext> GetCurrentContextAsync(CancellationToken cancellationToken);
}

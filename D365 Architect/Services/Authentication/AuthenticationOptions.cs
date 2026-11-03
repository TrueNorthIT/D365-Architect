namespace D365Architect.Services.Authentication;

/// <summary>
/// Configuration for the Microsoft Entra ID app used for interactive
/// sign-in. Defaults to the client ID Microsoft documents for building
/// console/native apps that authenticate to Dataverse:
/// https://learn.microsoft.com/power-apps/developer/data-platform/authenticate-oauth
///
/// Some tenants require sign-in through their own app registration instead
/// (e.g. because of Conditional Access policies) — override with the
/// d365architect_CLIENT_ID / d365architect_AUTHORITY environment variables in that case.
///
/// Only applies while d365architect_AUTH_MODE is unset. Once it's set,
/// d365architect_CLIENT_ID instead means the service principal or managed
/// identity to sign in as non-interactively (see
/// <see cref="NonInteractiveAuthSettingsResolver"/>), and this interactive
/// app isn't used at all.
/// </summary>
public sealed record AuthenticationOptions(string ClientId, string Authority)
{
    private const string DefaultClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";
    private const string DefaultAuthority = "https://login.microsoftonline.com/organizations";

    public static AuthenticationOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("d365architect_CLIENT_ID") ?? DefaultClientId,
        Environment.GetEnvironmentVariable("d365architect_AUTHORITY") ?? DefaultAuthority);
}

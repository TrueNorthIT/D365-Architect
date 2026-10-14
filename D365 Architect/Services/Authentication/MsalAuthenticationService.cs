using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace D365Architect.Services.Authentication;

/// <summary>
/// Signs in via MSAL.NET using the system browser (no embedded webview, so
/// no extra runtime dependency), and persists the token cache to disk
/// (encrypted at rest via the OS keychain/DPAPI, courtesy of
/// Microsoft.Identity.Client.Extensions.Msal) so a later CLI invocation —
/// a separate process — can reuse the sign-in instead of prompting again.
///
/// Which environment/account is "current" is tracked separately in a small
/// non-secret profile file next to the token cache.
/// </summary>
public sealed class MsalAuthenticationService(AuthenticationOptions options) : IAuthenticationService
{
    private const string CacheFileName = "msal.cache";
    private const string ProfileFileName = "profile.json";

    private readonly Lazy<Task<IPublicClientApplication>> _application = new(BuildApplicationAsync(options));

    public async Task<AuthenticatedContext> LoginInteractiveAsync(Uri environmentUrl, CancellationToken cancellationToken)
    {
        var app = await _application.Value;

        AuthenticationResult result;
        try
        {
            result = await app.AcquireTokenInteractive([ToDefaultScope(environmentUrl)])
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync(cancellationToken);
        }
        catch (MsalException ex)
        {
            throw new AuthenticationRequiredException($"Sign-in failed: {ex.Message}", ex);
        }

        await SaveProfileAsync(new StoredProfile(environmentUrl.ToString(), result.Account.Username), cancellationToken);

        return new AuthenticatedContext(environmentUrl, result.Account.Username, result.AccessToken);
    }

    public async Task<AuthenticatedContext> GetCurrentContextAsync(CancellationToken cancellationToken)
    {
        var profile = await LoadProfileAsync(cancellationToken)
            ?? throw new AuthenticationRequiredException("Not signed in. Run 'auth login --url <environment-url>' first.");

        var app = await _application.Value;
        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault(a => string.Equals(a.Username, profile.Username, StringComparison.OrdinalIgnoreCase))
            ?? throw new AuthenticationRequiredException("Your sign-in could not be found. Run 'auth login --url <environment-url>' again.");

        var environmentUrl = new Uri(profile.EnvironmentUrl);

        try
        {
            var result = await app.AcquireTokenSilent([ToDefaultScope(environmentUrl)], account).ExecuteAsync(cancellationToken);
            return new AuthenticatedContext(environmentUrl, result.Account.Username, result.AccessToken);
        }
        catch (MsalUiRequiredException)
        {
            throw new AuthenticationRequiredException("Your sign-in has expired. Run 'auth login --url <environment-url>' again.");
        }
    }

    private static string ToDefaultScope(Uri environmentUrl) => $"{environmentUrl.GetLeftPart(UriPartial.Authority)}/.default";

    private static Func<Task<IPublicClientApplication>> BuildApplicationAsync(AuthenticationOptions options) => async () =>
    {
        var app = PublicClientApplicationBuilder.Create(options.ClientId)
            .WithAuthority(options.Authority)
            .WithDefaultRedirectUri()
            .Build();

        var storageProperties = new StorageCreationPropertiesBuilder(CacheFileName, GetAppDataDirectory()).Build();
        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
        cacheHelper.RegisterCache(app.UserTokenCache);

        return app;
    };

    private static string GetAppDataDirectory()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "d365architect");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task SaveProfileAsync(StoredProfile profile, CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetAppDataDirectory(), ProfileFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, cancellationToken: cancellationToken);
    }

    private static async Task<StoredProfile?> LoadProfileAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetAppDataDirectory(), ProfileFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<StoredProfile>(stream, cancellationToken: cancellationToken);
    }

    private sealed record StoredProfile(string EnvironmentUrl, string Username);
}

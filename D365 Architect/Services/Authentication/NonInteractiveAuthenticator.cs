using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Azure.Identity;

namespace D365Architect.Services.Authentication;

/// <summary>
/// Signs in from <see cref="NonInteractiveAuthSettings"/> (i.e. from
/// environment variables, for pipelines) via the matching Azure.Identity
/// credential — never a browser, and never touching the saved `auth login`
/// profile or MSAL token cache. Stateless: every CLI invocation gets a fresh
/// token, since each one is its own process anyway.
///
/// Every failure comes out as <see cref="AuthenticationRequiredException"/>
/// so commands show a plain error rather than a stack trace. Azure.Identity's
/// own messages don't include the client secret or certificate password,
/// but they're redacted here as well in case a future version ever does,
/// and the original exception isn't attached for the same reason.
/// </summary>
public sealed class NonInteractiveAuthenticator
{
    public async Task<AuthenticatedContext> GetContextAsync(NonInteractiveAuthSettings settings, CancellationToken cancellationToken)
    {
        var modeName = NonInteractiveAuthSettingsResolver.GetModeName(settings.Mode);

        AccessToken token;
        try
        {
            var credential = CreateCredential(settings);
            token = await credential.GetTokenAsync(
                new TokenRequestContext([MsalAuthenticationService.ToDefaultScope(settings.EnvironmentUrl)], tenantId: settings.TenantId),
                cancellationToken);
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or ArgumentException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            // CredentialUnavailableException (e.g. Azure CLI not installed or
            // not signed in, no managed identity endpoint) derives from
            // AuthenticationFailedException, so is covered here too.
            throw new AuthenticationRequiredException(
                Redact($"Non-interactive sign-in ({NonInteractiveAuthSettingsResolver.ModeVariable}={modeName}) failed: {DescribeFailure(ex)}", settings));
        }

        var identity = AccessTokenIdentity.Describe(token.Token) ?? settings.ClientId ?? modeName;
        return new AuthenticatedContext(settings.EnvironmentUrl, identity, token.Token, settings.Mode);
    }

    private static TokenCredential CreateCredential(NonInteractiveAuthSettings settings) => settings.Mode switch
    {
        NonInteractiveAuthMode.ClientSecret =>
            new ClientSecretCredential(settings.TenantId, settings.ClientId, settings.ClientSecret),

        NonInteractiveAuthMode.Certificate => CreateCertificateCredential(settings),

        // Uses whatever az is signed in as — including whichever
        // AZURE_CONFIG_DIR this process inherited, so it never needs (or
        // changes) the machine-wide az login.
        NonInteractiveAuthMode.AzureCli =>
            new AzureCliCredential(new AzureCliCredentialOptions { TenantId = settings.TenantId }),

        NonInteractiveAuthMode.ManagedIdentity =>
            new ManagedIdentityCredential(settings.ClientId is null
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.FromUserAssignedClientId(settings.ClientId)),

        // Options set explicitly rather than left to WorkloadIdentityCredential's
        // own AZURE_* environment variable lookup, so the d365architect_*
        // variables are the only ones that count.
        NonInteractiveAuthMode.WorkloadIdentity =>
            new WorkloadIdentityCredential(new WorkloadIdentityCredentialOptions
            {
                TenantId = settings.TenantId,
                ClientId = settings.ClientId,
                TokenFilePath = settings.FederatedTokenFile,
            }),

        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Mode, "Unknown non-interactive authentication mode."),
    };

    private static ClientCertificateCredential CreateCertificateCredential(NonInteractiveAuthSettings settings)
    {
        if (settings.CertificateThumbprint is not null)
        {
            return new ClientCertificateCredential(settings.TenantId, settings.ClientId, FindCertificateByThumbprint(settings.CertificateThumbprint));
        }

        var path = settings.CertificatePath!;
        if (!File.Exists(path))
        {
            throw new AuthenticationRequiredException(
                $"{NonInteractiveAuthSettingsResolver.CertificatePathVariable} '{path}' does not exist.");
        }

        // Without a password, Azure.Identity loads the file itself (and so
        // also accepts a .pem); a password-protected .pfx has to be opened here.
        return settings.CertificatePassword is null
            ? new ClientCertificateCredential(settings.TenantId, settings.ClientId, path)
            : new ClientCertificateCredential(settings.TenantId, settings.ClientId, X509CertificateLoader.LoadPkcs12FromFile(path, settings.CertificatePassword));
    }

    /// <summary>Looks in CurrentUser\My, then LocalMachine\My, for a certificate with a private key.</summary>
    private static X509Certificate2 FindCertificateByThumbprint(string thumbprint)
    {
        // Thumbprints copied out of certmgr often carry spaces (or an
        // invisible left-to-right mark at the start), neither of which is
        // part of the thumbprint itself.
        var normalised = new string(thumbprint.Where(char.IsAsciiHexDigit).ToArray());

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var match = store.Certificates.Find(X509FindType.FindByThumbprint, normalised, validOnly: false)
                    .FirstOrDefault(c => c.HasPrivateKey);
                if (match is not null)
                {
                    return match;
                }
            }
            catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
            {
                // That store doesn't exist (or isn't supported) on this
                // machine — carry on to the next one.
            }
        }

        throw new AuthenticationRequiredException(
            $"No certificate with a private key matching {NonInteractiveAuthSettingsResolver.CertificateThumbprintVariable} '{thumbprint}' was found in CurrentUser\\My or LocalMachine\\My.");
    }

    /// <summary>
    /// The exception's message plus any inner exception's that adds to it —
    /// Azure.Identity's own message is often just "ClientSecretCredential
    /// authentication failed:", with the useful AADSTS detail only on the
    /// inner MSAL exception.
    /// </summary>
    private static string DescribeFailure(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message.Trim();
            if (message.Length > 0 && !parts.Any(p => p.Contains(message, StringComparison.Ordinal)))
            {
                parts.Add(message);
            }
        }

        return string.Join(" ", parts);
    }

    private static string Redact(string message, NonInteractiveAuthSettings settings)
    {
        foreach (var secret in new[] { settings.ClientSecret, settings.CertificatePassword })
        {
            if (!string.IsNullOrEmpty(secret))
            {
                message = message.Replace(secret, "***", StringComparison.Ordinal);
            }
        }

        return message;
    }
}

namespace D365Architect.Services.Authentication;

/// <summary>
/// How a non-interactive (pipeline) sign-in gets its credential — the value
/// of the d365architect_AUTH_MODE environment variable. See
/// <see cref="NonInteractiveAuthSettingsResolver"/> for which other
/// variables each mode needs.
/// </summary>
public enum NonInteractiveAuthMode
{
    /// <summary>A service principal with a client secret (<c>clientsecret</c>).</summary>
    ClientSecret,

    /// <summary>A service principal with a certificate, from a file or the Windows certificate store (<c>certificate</c>).</summary>
    Certificate,

    /// <summary>Whoever the Azure CLI is already signed in as (<c>azurecli</c>).</summary>
    AzureCli,

    /// <summary>A system- or user-assigned managed identity (<c>managedidentity</c>).</summary>
    ManagedIdentity,

    /// <summary>Workload identity federation from a federated token file (<c>workloadidentity</c>).</summary>
    WorkloadIdentity,
}

/// <summary>
/// Everything a non-interactive sign-in needs, read from environment
/// variables by <see cref="NonInteractiveAuthSettingsResolver"/>. Fields a
/// mode doesn't use are left null. Never logged or printed as a whole: it
/// carries the client secret / certificate password.
/// </summary>
public sealed record NonInteractiveAuthSettings(
    NonInteractiveAuthMode Mode,
    Uri EnvironmentUrl,
    string? TenantId,
    string? ClientId,
    string? ClientSecret,
    string? CertificatePath,
    string? CertificatePassword,
    string? CertificateThumbprint,
    string? FederatedTokenFile);

/// <summary>
/// Turns the d365architect_* environment variables into
/// <see cref="NonInteractiveAuthSettings"/>, so a pipeline can run any
/// command without a browser or a prior `auth login`.
///
/// Pure (the lookup is passed in rather than read from the process
/// environment) so every combination can be unit-tested. Empty and
/// whitespace-only values count as unset throughout, since a pipeline
/// variable that was never filled in usually arrives as an empty string
/// rather than not at all.
///
/// Error messages name the variables involved but never echo the client
/// secret or certificate password, so a misconfigured pipeline can't print
/// either into its log.
/// </summary>
public static class NonInteractiveAuthSettingsResolver
{
    public const string ModeVariable = "d365architect_AUTH_MODE";
    public const string UrlVariable = "d365architect_URL";
    public const string TenantIdVariable = "d365architect_TENANT_ID";
    public const string ClientIdVariable = "d365architect_CLIENT_ID";
    public const string ClientSecretVariable = "d365architect_CLIENT_SECRET";
    public const string CertificatePathVariable = "d365architect_CERTIFICATE_PATH";
    public const string CertificatePasswordVariable = "d365architect_CERTIFICATE_PASSWORD";
    public const string CertificateThumbprintVariable = "d365architect_CERTIFICATE_THUMBPRINT";
    public const string FederatedTokenFileVariable = "d365architect_FEDERATED_TOKEN_FILE";

    /// <summary>The Azure SDK's own variable, set by e.g. AKS workload identity — used when <see cref="FederatedTokenFileVariable"/> isn't.</summary>
    public const string AzureFederatedTokenFileVariable = "AZURE_FEDERATED_TOKEN_FILE";

    // Explicit rather than Enum.TryParse, which would also accept numbers
    // ("1") — the accepted spellings are exactly the documented ones.
    private static readonly IReadOnlyList<(string Name, NonInteractiveAuthMode Mode)> Modes =
    [
        ("clientsecret", NonInteractiveAuthMode.ClientSecret),
        ("certificate", NonInteractiveAuthMode.Certificate),
        ("azurecli", NonInteractiveAuthMode.AzureCli),
        ("managedidentity", NonInteractiveAuthMode.ManagedIdentity),
        ("workloadidentity", NonInteractiveAuthMode.WorkloadIdentity),
    ];

    /// <summary>
    /// Returns null when d365architect_AUTH_MODE is unset or blank, meaning
    /// the caller should use the saved interactive sign-in exactly as
    /// before. Otherwise returns the settings for that mode, or throws
    /// <see cref="AuthenticationRequiredException"/> naming whatever is
    /// missing or invalid.
    /// </summary>
    public static NonInteractiveAuthSettings? Resolve(Func<string, string?> getEnvironmentVariable)
    {
        string? Read(string name)
        {
            var value = getEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var modeValue = Read(ModeVariable);
        if (modeValue is null)
        {
            return null;
        }

        var mode = ParseMode(modeValue);
        var modeName = GetModeName(mode);
        var environmentUrl = ParseUrl(Read(UrlVariable), modeName);

        var tenantId = Read(TenantIdVariable);
        var clientId = Read(ClientIdVariable);
        var missing = new List<string>();

        void Require(string name, string? value)
        {
            if (value is null)
            {
                missing.Add(name);
            }
        }

        var settings = new NonInteractiveAuthSettings(mode, environmentUrl, null, null, null, null, null, null, null);

        switch (mode)
        {
            case NonInteractiveAuthMode.ClientSecret:
                var clientSecret = Read(ClientSecretVariable);
                Require(TenantIdVariable, tenantId);
                Require(ClientIdVariable, clientId);
                Require(ClientSecretVariable, clientSecret);
                settings = settings with { TenantId = tenantId, ClientId = clientId, ClientSecret = clientSecret };
                break;

            case NonInteractiveAuthMode.Certificate:
                Require(TenantIdVariable, tenantId);
                Require(ClientIdVariable, clientId);
                var certificatePath = Read(CertificatePathVariable);
                var thumbprint = Read(CertificateThumbprintVariable);
                if ((certificatePath is null) == (thumbprint is null))
                {
                    throw new AuthenticationRequiredException(
                        $"{ModeVariable} is '{modeName}': set exactly one of {CertificatePathVariable} (a .pfx/.pem file) or {CertificateThumbprintVariable} (a certificate in the Windows certificate store), not {(certificatePath is null ? "neither" : "both")}.");
                }

                settings = settings with
                {
                    TenantId = tenantId,
                    ClientId = clientId,
                    CertificatePath = certificatePath,
                    CertificatePassword = certificatePath is null ? null : Read(CertificatePasswordVariable),
                    CertificateThumbprint = thumbprint,
                };
                break;

            case NonInteractiveAuthMode.AzureCli:
                settings = settings with { TenantId = tenantId };
                break;

            case NonInteractiveAuthMode.ManagedIdentity:
                // No client ID means the system-assigned identity; a tenant
                // ID means nothing to a managed identity, so it's ignored.
                settings = settings with { ClientId = clientId };
                break;

            case NonInteractiveAuthMode.WorkloadIdentity:
                var tokenFile = Read(FederatedTokenFileVariable) ?? Read(AzureFederatedTokenFileVariable);
                Require(TenantIdVariable, tenantId);
                Require(ClientIdVariable, clientId);
                Require(FederatedTokenFileVariable, tokenFile);
                settings = settings with { TenantId = tenantId, ClientId = clientId, FederatedTokenFile = tokenFile };
                break;
        }

        if (missing.Count > 0)
        {
            throw new AuthenticationRequiredException(
                $"{ModeVariable} is '{modeName}', but {(missing.Count == 1 ? "this environment variable is" : "these environment variables are")} not set: {string.Join(", ", missing)}.");
        }

        return settings;
    }

    /// <summary>The documented d365architect_AUTH_MODE spelling of <paramref name="mode"/>, e.g. <c>clientsecret</c>.</summary>
    public static string GetModeName(NonInteractiveAuthMode mode) => Modes.First(m => m.Mode == mode).Name;

    private static NonInteractiveAuthMode ParseMode(string value)
    {
        foreach (var (name, mode) in Modes)
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        throw new AuthenticationRequiredException(
            $"'{value}' is not a valid {ModeVariable}. Use one of: {string.Join(", ", Modes.Select(m => m.Name))} (or leave it unset to use the 'auth login' sign-in).");
    }

    private static Uri ParseUrl(string? value, string modeName)
    {
        if (value is null)
        {
            throw new AuthenticationRequiredException(
                $"{ModeVariable} is '{modeName}', but {UrlVariable} is not set. Set it to the environment's URL, e.g. https://yourorg.crm.dynamics.com");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new AuthenticationRequiredException(
                $"{UrlVariable} '{value}' is not a valid https:// environment URL, e.g. https://yourorg.crm.dynamics.com");
        }

        return url;
    }
}

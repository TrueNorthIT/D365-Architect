using D365Architect.Services.Authentication;
using Xunit;

namespace D365Architect.Tests.Services.Authentication;

/// <summary>
/// Covers <see cref="NonInteractiveAuthSettingsResolver.Resolve"/>, the pure
/// environment-variable to settings step of non-interactive (pipeline) sign-in.
/// Production code: D365 Architect/Services/Authentication/NonInteractiveAuthSettings.cs.
///
/// Every test feeds a dictionary-backed lookup (ordinal, so variable names are
/// case-sensitive exactly as the contract says) and never touches the real
/// process environment. Credential construction and token acquisition are not
/// covered here; they are verified live.
///
/// Requirements covered (REQ-10 whoami and REQ-12 docs are outside this level):
///   REQ-1  Any command can sign in purely from environment variables.
///   REQ-2  Sign in as a service principal with a client secret.
///   REQ-3  Sign in as a service principal with a certificate (.pfx path, optional password, or thumbprint).
///   REQ-4  Sign in by reusing an existing Azure CLI login.
///   REQ-5  Sign in with a managed identity (system-assigned, or user-assigned when a client ID is given).
///   REQ-6  Sign in with workload identity federation from a federated token file.
///   REQ-7  When the mode variable is set, the saved interactive sign-in is ignored entirely.
///   REQ-8  Missing/invalid settings fail fast naming what is missing; never print the secret.
///   REQ-9  An unknown mode value is rejected with the list of valid modes.
///   REQ-11 Interactive flow unchanged when no mode is set.
/// </summary>
public sealed class NonInteractiveAuthSettingsResolverTests
{
    private const string Url = "https://contoso.crm.dynamics.com";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Client = "22222222-2222-2222-2222-222222222222";
    private const string DistinctiveSecret = "s3cr3t-VALUE-do-not-leak-7731";
    private const string DistinctivePassword = "pfx-PASSWORD-do-not-leak-9042";

    private static Func<string, string?> Env(params (string Name, string? Value)[] variables)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            map[name] = value;
        }

        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>A complete, valid environment for the given mode, as a name/value list a test can edit.</summary>
    private static List<(string Name, string? Value)> ValidEnv(string mode)
    {
        var vars = new List<(string Name, string? Value)>
        {
            ("d365architect_AUTH_MODE", mode),
            ("d365architect_URL", Url),
        };

        switch (mode.ToLowerInvariant())
        {
            case "clientsecret":
                vars.Add(("d365architect_TENANT_ID", Tenant));
                vars.Add(("d365architect_CLIENT_ID", Client));
                vars.Add(("d365architect_CLIENT_SECRET", DistinctiveSecret));
                break;
            case "certificate":
                vars.Add(("d365architect_TENANT_ID", Tenant));
                vars.Add(("d365architect_CLIENT_ID", Client));
                vars.Add(("d365architect_CERTIFICATE_PATH", "C:\\certs\\app.pfx"));
                break;
            case "workloadidentity":
                vars.Add(("d365architect_TENANT_ID", Tenant));
                vars.Add(("d365architect_CLIENT_ID", Client));
                vars.Add(("d365architect_FEDERATED_TOKEN_FILE", "/var/run/token"));
                break;
        }

        return vars;
    }

    private static List<(string Name, string? Value)> With(List<(string Name, string? Value)> vars, string name, string? value)
    {
        var copy = vars.Where(v => v.Name != name).ToList();
        copy.Add((name, value));
        return copy;
    }

    private static List<(string Name, string? Value)> Without(List<(string Name, string? Value)> vars, string name) =>
        vars.Where(v => v.Name != name).ToList();

    private static Func<string, string?> AsEnv(List<(string Name, string? Value)> vars) => Env(vars.ToArray());

    // ---------------------------------------------------------------- REQ-11, REQ-7: mode unset or set

    // Given no d365architect_AUTH_MODE (absent, empty or whitespace), Resolve returns null so the
    // caller keeps using the saved interactive profile.
    [Theory]
    [Trait("Requirement", "REQ-11")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ModeUnsetOrBlank_ReturnsNullSoInteractiveProfileIsUsed(string? mode)
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(Env(("d365architect_AUTH_MODE", mode)));

        Assert.True(settings is null, "REQ-11: with d365architect_AUTH_MODE unset or blank the resolver must return null (interactive saved profile flow unchanged).");
    }

    // d365architect_CLIENT_ID already overrides the interactive app ID when no mode is set, so other
    // variables being present must not trigger non-interactive auth or validation errors.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public void Resolve_ModeUnsetButOtherVariablesSet_ReturnsNullWithoutThrowing()
    {
        var env = Env(
            ("d365architect_CLIENT_ID", Client),
            ("d365architect_CLIENT_SECRET", DistinctiveSecret),
            ("d365architect_TENANT_ID", Tenant),
            ("d365architect_URL", "http://not-https.example"));

        var settings = NonInteractiveAuthSettingsResolver.Resolve(env);

        Assert.True(settings is null, "REQ-11: other d365architect_ variables must be ignored by the resolver when d365architect_AUTH_MODE is unset.");
    }

    // The mode variable name is case-sensitive; a differently-cased name is not the mode variable.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public void Resolve_ModeVariableNameWithWrongCase_IsTreatedAsUnset()
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(Env(("D365ARCHITECT_AUTH_MODE", "clientsecret")));

        Assert.True(settings is null, "Variable names are exact and case-sensitive: 'D365ARCHITECT_AUTH_MODE' must not switch on non-interactive auth.");
    }

    // REQ-7: once the mode is set the resolver yields settings (non-null), which is the signal that
    // the saved interactive sign-in is bypassed entirely.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public void Resolve_ModeSet_ReturnsSettingsSoSavedInteractiveSignInIsBypassed()
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(ValidEnv("clientsecret")));

        Assert.True(settings is not null, "REQ-7: when d365architect_AUTH_MODE is set the resolver must return settings, not null (null means 'use the saved profile').");
    }

    // ---------------------------------------------------------------- REQ-2: client secret

    // Given mode=clientsecret with URL, tenant, client ID and secret, Resolve returns those values.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    [Trait("Requirement", "REQ-2")]
    public void Resolve_ClientSecretMode_PopulatesUrlTenantClientAndSecret()
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(ValidEnv("clientsecret")));

        Assert.NotNull(settings);
        Assert.Equal(NonInteractiveAuthMode.ClientSecret, settings!.Mode);
        Assert.Equal(new Uri(Url), settings.EnvironmentUrl);
        Assert.Equal(Tenant, settings.TenantId);
        Assert.Equal(Client, settings.ClientId);
        Assert.Equal(DistinctiveSecret, settings.ClientSecret);
    }

    // ---------------------------------------------------------------- REQ-3: certificate

    // Given mode=certificate with a .pfx path and password, Resolve returns path and password, no thumbprint.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public void Resolve_CertificateModeWithPfxPathAndPassword_PopulatesPathAndPassword()
    {
        var vars = With(ValidEnv("certificate"), "d365architect_CERTIFICATE_PASSWORD", DistinctivePassword);

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal(NonInteractiveAuthMode.Certificate, settings!.Mode);
        Assert.Equal(new Uri(Url), settings.EnvironmentUrl);
        Assert.Equal(Tenant, settings.TenantId);
        Assert.Equal(Client, settings.ClientId);
        Assert.Equal("C:\\certs\\app.pfx", settings.CertificatePath);
        Assert.Equal(DistinctivePassword, settings.CertificatePassword);
        Assert.Null(settings.CertificateThumbprint);
    }

    // The certificate password is optional.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public void Resolve_CertificateModeWithPfxPathAndNoPassword_ResolvesWithNullPassword()
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(ValidEnv("certificate")));

        Assert.NotNull(settings);
        Assert.Equal("C:\\certs\\app.pfx", settings!.CertificatePath);
        Assert.Null(settings.CertificatePassword);
    }

    // Given mode=certificate with a thumbprint instead of a path, Resolve returns the thumbprint and no path.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public void Resolve_CertificateModeWithThumbprint_PopulatesThumbprintAndNoPath()
    {
        var vars = With(Without(ValidEnv("certificate"), "d365architect_CERTIFICATE_PATH"), "d365architect_CERTIFICATE_THUMBPRINT", "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12");

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal(NonInteractiveAuthMode.Certificate, settings!.Mode);
        Assert.Equal("AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12", settings.CertificateThumbprint);
        Assert.Null(settings.CertificatePath);
    }

    // Exactly one of path/thumbprint: both is an error naming both variables.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Requirement", "REQ-8")]
    public void Resolve_CertificateModeWithBothPathAndThumbprint_ThrowsNamingBothVariables()
    {
        var vars = With(ValidEnv("certificate"), "d365architect_CERTIFICATE_THUMBPRINT", "AB12CD34");

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.Contains("d365architect_CERTIFICATE_PATH", ex.Message);
        Assert.Contains("d365architect_CERTIFICATE_THUMBPRINT", ex.Message);
    }

    // Exactly one of path/thumbprint: neither is an error naming both variables.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Requirement", "REQ-8")]
    public void Resolve_CertificateModeWithNeitherPathNorThumbprint_ThrowsNamingBothVariables()
    {
        var vars = Without(ValidEnv("certificate"), "d365architect_CERTIFICATE_PATH");

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.Contains("d365architect_CERTIFICATE_PATH", ex.Message);
        Assert.Contains("d365architect_CERTIFICATE_THUMBPRINT", ex.Message);
    }

    // A blank path counts as unset, so a blank path alongside a thumbprint is valid (thumbprint only).
    [Fact]
    [Trait("Requirement", "REQ-3")]
    [Trait("Requirement", "REQ-8")]
    public void Resolve_CertificateModeWithBlankPathAndThumbprint_TreatsBlankPathAsUnset()
    {
        var vars = With(With(ValidEnv("certificate"), "d365architect_CERTIFICATE_PATH", "   "), "d365architect_CERTIFICATE_THUMBPRINT", "AB12CD34");

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal("AB12CD34", settings!.CertificateThumbprint);
        Assert.Null(settings.CertificatePath);
    }

    // ---------------------------------------------------------------- REQ-4: Azure CLI

    // Given mode=azurecli with only a URL, Resolve succeeds with no tenant or client ID.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public void Resolve_AzureCliModeWithOnlyUrl_ResolvesWithNoTenant()
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(ValidEnv("azurecli")));

        Assert.NotNull(settings);
        Assert.Equal(NonInteractiveAuthMode.AzureCli, settings!.Mode);
        Assert.Equal(new Uri(Url), settings.EnvironmentUrl);
        Assert.Null(settings.TenantId);
    }

    // Tenant is optional for azurecli but passed through when supplied.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public void Resolve_AzureCliModeWithTenant_PassesTenantThrough()
    {
        var vars = With(ValidEnv("azurecli"), "d365architect_TENANT_ID", Tenant);

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal(Tenant, settings!.TenantId);
    }

    // ---------------------------------------------------------------- REQ-5: managed identity

    // System-assigned: mode=managedidentity with no client ID resolves with ClientId null.
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public void Resolve_ManagedIdentityModeWithoutClientId_ResolvesAsSystemAssigned()
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(ValidEnv("managedidentity")));

        Assert.NotNull(settings);
        Assert.Equal(NonInteractiveAuthMode.ManagedIdentity, settings!.Mode);
        Assert.Equal(new Uri(Url), settings.EnvironmentUrl);
        Assert.Null(settings.ClientId);
    }

    // User-assigned: a client ID present is carried through.
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public void Resolve_ManagedIdentityModeWithClientId_ResolvesAsUserAssignedWithThatClientId()
    {
        var vars = With(ValidEnv("managedidentity"), "d365architect_CLIENT_ID", Client);

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal(Client, settings!.ClientId);
    }

    // A tenant ID supplied alongside a managed identity is ignored, not an error.
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public void Resolve_ManagedIdentityModeWithTenantIdSet_DoesNotThrow()
    {
        var vars = With(ValidEnv("managedidentity"), "d365architect_TENANT_ID", Tenant);

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal(NonInteractiveAuthMode.ManagedIdentity, settings!.Mode);
    }

    // ---------------------------------------------------------------- REQ-6: workload identity

    // Given mode=workloadidentity with tenant, client and d365architect_FEDERATED_TOKEN_FILE, Resolve returns the file.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public void Resolve_WorkloadIdentityMode_PopulatesTenantClientAndFederatedTokenFile()
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(ValidEnv("workloadidentity")));

        Assert.NotNull(settings);
        Assert.Equal(NonInteractiveAuthMode.WorkloadIdentity, settings!.Mode);
        Assert.Equal(new Uri(Url), settings.EnvironmentUrl);
        Assert.Equal(Tenant, settings.TenantId);
        Assert.Equal(Client, settings.ClientId);
        Assert.Equal("/var/run/token", settings.FederatedTokenFile);
    }

    // Falls back to AZURE_FEDERATED_TOKEN_FILE when the d365architect_ variable is absent.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public void Resolve_WorkloadIdentityModeWithOnlyAzureFederatedTokenFile_FallsBackToIt()
    {
        var vars = With(Without(ValidEnv("workloadidentity"), "d365architect_FEDERATED_TOKEN_FILE"), "AZURE_FEDERATED_TOKEN_FILE", "/azure/token");

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal("/azure/token", settings!.FederatedTokenFile);
    }

    // A blank d365architect_FEDERATED_TOKEN_FILE counts as unset, so the fallback applies.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    [Trait("Requirement", "REQ-8")]
    public void Resolve_WorkloadIdentityModeWithBlankPrimaryTokenFile_FallsBackToAzureVariable()
    {
        var vars = With(With(ValidEnv("workloadidentity"), "d365architect_FEDERATED_TOKEN_FILE", "  "), "AZURE_FEDERATED_TOKEN_FILE", "/azure/token");

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal("/azure/token", settings!.FederatedTokenFile);
    }

    // When both are set, the d365architect_ variable wins.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public void Resolve_WorkloadIdentityModeWithBothTokenFileVariables_PrefersD365ArchitectVariable()
    {
        var vars = With(ValidEnv("workloadidentity"), "AZURE_FEDERATED_TOKEN_FILE", "/azure/token");

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Equal("/var/run/token", settings!.FederatedTokenFile);
    }

    // Neither token file variable set: error naming the d365architect_ variable.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    [Trait("Requirement", "REQ-8")]
    public void Resolve_WorkloadIdentityModeWithNoTokenFile_ThrowsNamingTheVariable()
    {
        var vars = Without(ValidEnv("workloadidentity"), "d365architect_FEDERATED_TOKEN_FILE");

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.Contains("d365architect_FEDERATED_TOKEN_FILE", ex.Message);
    }

    // ---------------------------------------------------------------- mode parsing (REQ-1, REQ-9)

    // Mode values are case-insensitive.
    [Theory]
    [Trait("Requirement", "REQ-1")]
    [InlineData("clientsecret", NonInteractiveAuthMode.ClientSecret)]
    [InlineData("ClientSecret", NonInteractiveAuthMode.ClientSecret)]
    [InlineData("CLIENTSECRET", NonInteractiveAuthMode.ClientSecret)]
    [InlineData("Certificate", NonInteractiveAuthMode.Certificate)]
    [InlineData("AzureCli", NonInteractiveAuthMode.AzureCli)]
    [InlineData("AZURECLI", NonInteractiveAuthMode.AzureCli)]
    [InlineData("ManagedIdentity", NonInteractiveAuthMode.ManagedIdentity)]
    [InlineData("WorkloadIdentity", NonInteractiveAuthMode.WorkloadIdentity)]
    public void Resolve_ModeValueInAnyCase_ResolvesToMatchingMode(string modeValue, NonInteractiveAuthMode expected)
    {
        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(ValidEnv(modeValue)));

        Assert.NotNull(settings);
        Assert.Equal(expected, settings!.Mode);
    }

    // REQ-9: unknown mode throws, and the message lists every valid mode.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public void Resolve_UnknownMode_ThrowsListingAllFiveValidModes()
    {
        var env = Env(("d365architect_AUTH_MODE", "interactive"), ("d365architect_URL", Url));

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(env));

        Assert.Contains("d365architect_AUTH_MODE", ex.Message);
        foreach (var valid in new[] { "clientsecret", "certificate", "azurecli", "managedidentity", "workloadidentity" })
        {
            Assert.Contains(valid, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------------------------------------------------------------- REQ-8: missing and blank variables

    // d365architect_URL is required for every mode.
    [Theory]
    [Trait("Requirement", "REQ-8")]
    [InlineData("clientsecret")]
    [InlineData("certificate")]
    [InlineData("azurecli")]
    [InlineData("managedidentity")]
    [InlineData("workloadidentity")]
    public void Resolve_UrlMissing_ThrowsNamingUrlVariable(string mode)
    {
        var vars = Without(ValidEnv(mode), "d365architect_URL");

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.Contains("d365architect_URL", ex.Message);
    }

    // Each required variable, per mode, is reported by name when it is absent.
    [Theory]
    [Trait("Requirement", "REQ-8")]
    [InlineData("clientsecret", "d365architect_TENANT_ID")]
    [InlineData("clientsecret", "d365architect_CLIENT_ID")]
    [InlineData("clientsecret", "d365architect_CLIENT_SECRET")]
    [InlineData("certificate", "d365architect_TENANT_ID")]
    [InlineData("certificate", "d365architect_CLIENT_ID")]
    [InlineData("workloadidentity", "d365architect_TENANT_ID")]
    [InlineData("workloadidentity", "d365architect_CLIENT_ID")]
    public void Resolve_RequiredVariableMissing_ThrowsNamingThatVariable(string mode, string missingVariable)
    {
        var vars = Without(ValidEnv(mode), missingVariable);

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.Contains(missingVariable, ex.Message);
    }

    // Empty and whitespace-only values count as unset, so they fail the same way as an absent variable.
    [Theory]
    [Trait("Requirement", "REQ-8")]
    [InlineData("clientsecret", "d365architect_URL", "")]
    [InlineData("clientsecret", "d365architect_URL", "   ")]
    [InlineData("clientsecret", "d365architect_TENANT_ID", "")]
    [InlineData("clientsecret", "d365architect_TENANT_ID", "  ")]
    [InlineData("clientsecret", "d365architect_CLIENT_ID", "")]
    [InlineData("clientsecret", "d365architect_CLIENT_ID", " ")]
    [InlineData("clientsecret", "d365architect_CLIENT_SECRET", "")]
    [InlineData("clientsecret", "d365architect_CLIENT_SECRET", "   ")]
    [InlineData("certificate", "d365architect_CLIENT_ID", "")]
    [InlineData("workloadidentity", "d365architect_TENANT_ID", " ")]
    [InlineData("workloadidentity", "d365architect_FEDERATED_TOKEN_FILE", "")]
    [InlineData("azurecli", "d365architect_URL", "  ")]
    [InlineData("managedidentity", "d365architect_URL", "")]
    public void Resolve_RequiredVariableBlank_ThrowsNamingThatVariable(string mode, string blankVariable, string blankValue)
    {
        var vars = With(ValidEnv(mode), blankVariable, blankValue);

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.Contains(blankVariable, ex.Message);
    }

    // Blank optional values are treated as unset rather than carried through as empty strings.
    [Theory]
    [Trait("Requirement", "REQ-8")]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ManagedIdentityModeWithBlankClientId_ResolvesAsSystemAssigned(string blank)
    {
        var vars = With(ValidEnv("managedidentity"), "d365architect_CLIENT_ID", blank);

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Null(settings!.ClientId);
    }

    [Theory]
    [Trait("Requirement", "REQ-8")]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_AzureCliModeWithBlankTenant_TreatsTenantAsUnset(string blank)
    {
        var vars = With(ValidEnv("azurecli"), "d365architect_TENANT_ID", blank);

        var settings = NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars));

        Assert.NotNull(settings);
        Assert.Null(settings!.TenantId);
    }

    // d365architect_URL must be an absolute https URL.
    [Theory]
    [Trait("Requirement", "REQ-8")]
    [InlineData("http://contoso.crm.dynamics.com")]
    [InlineData("contoso.crm.dynamics.com")]
    [InlineData("/api/data/v9.2")]
    [InlineData("not a url")]
    [InlineData("ftp://contoso.crm.dynamics.com")]
    public void Resolve_UrlNotAbsoluteHttps_ThrowsNamingUrlVariable(string badUrl)
    {
        var vars = With(ValidEnv("azurecli"), "d365architect_URL", badUrl);

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.Contains("d365architect_URL", ex.Message);
    }

    // ---------------------------------------------------------------- REQ-8: secrets never printed

    public static TheoryData<string> InvalidConfigurationsCarryingSecrets() => new()
    {
        "bad URL with client secret",
        "missing tenant with client secret",
        "missing client ID with client secret",
        "unknown mode with client secret",
        "both certificate path and thumbprint with password",
        "neither certificate path nor thumbprint, with password",
        "bad URL with certificate password",
    };

    private static List<(string Name, string? Value)> InvalidEnvCarryingSecrets(string scenario)
    {
        var vars = scenario switch
        {
            "bad URL with client secret" => With(ValidEnv("clientsecret"), "d365architect_URL", "http://insecure.example"),
            "missing tenant with client secret" => Without(ValidEnv("clientsecret"), "d365architect_TENANT_ID"),
            "missing client ID with client secret" => Without(ValidEnv("clientsecret"), "d365architect_CLIENT_ID"),
            "unknown mode with client secret" => With(ValidEnv("clientsecret"), "d365architect_AUTH_MODE", "bogus"),
            "both certificate path and thumbprint with password" => With(ValidEnv("certificate"), "d365architect_CERTIFICATE_THUMBPRINT", "AB12"),
            "neither certificate path nor thumbprint, with password" => Without(ValidEnv("certificate"), "d365architect_CERTIFICATE_PATH"),
            "bad URL with certificate password" => With(ValidEnv("certificate"), "d365architect_URL", "http://insecure.example"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
        };

        // Every scenario carries both secret values, whichever mode it is in.
        vars = With(vars, "d365architect_CLIENT_SECRET", DistinctiveSecret);
        return With(vars, "d365architect_CERTIFICATE_PASSWORD", DistinctivePassword);
    }

    // Every failure path with a client secret or certificate password present must keep those values
    // out of the exception message (and inner exception).
    [Theory]
    [Trait("Requirement", "REQ-8")]
    [MemberData(nameof(InvalidConfigurationsCarryingSecrets))]
    public void Resolve_InvalidConfigurationWhileSecretsAreSet_NeverIncludesSecretValuesInMessage(string scenario)
    {
        var vars = InvalidEnvCarryingSecrets(scenario);

        var ex = Assert.Throws<AuthenticationRequiredException>(() => NonInteractiveAuthSettingsResolver.Resolve(AsEnv(vars)));

        Assert.DoesNotContain(DistinctiveSecret, ex.ToString());
        Assert.DoesNotContain(DistinctivePassword, ex.ToString());
    }
}

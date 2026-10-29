using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using D365Architect.Services.Authentication;
using D365Architect.Services.Dataverse;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// A live test's connection to the developer's own environment: the real <see cref="DataverseClient"/> built as
/// <c>Program.cs</c> builds it, a token from the developer's existing <c>d365architect auth login</c> session (the app's own
/// <see cref="MsalAuthenticationService"/>, honouring <c>d365architect_AUTHORITY</c>/<c>d365architect_CLIENT_ID</c>), and the
/// few raw Web API calls a test needs for set-up, checks and clean-up that the app itself never makes (deleting a
/// table, creating and deleting a solution, reading solution components). Every component a test creates is named
/// <c>&lt;prefix&gt;_lt…</c> via <see cref="NewName"/> so a leftover is easy to spot.
/// </summary>
public sealed class LiveDataverse : IDisposable
{
    public const string EnvironmentVariable = "D365ARCHITECT_LIVE_ENV";
    public const string PrefixVariable = "D365ARCHITECT_LIVE_PREFIX";

    /// <summary>The environment URL and publisher prefix to test with; null (live tests skip) unless both are set.</summary>
    public static (Uri EnvironmentUrl, string Prefix)? Settings { get; } =
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } url && Environment.GetEnvironmentVariable(PrefixVariable) is { Length: > 0 } prefix
            ? (new Uri(url), prefix.Trim().ToLowerInvariant())
            : null;

    // Deleting a table can take well over HttpClient's default 100 s.
    private readonly HttpClient _raw = new() { Timeout = TimeSpan.FromMinutes(5) };

    private LiveDataverse(Uri environmentUrl, string prefix, string accessToken)
    {
        EnvironmentUrl = environmentUrl;
        Prefix = prefix;
        AccessToken = accessToken;
        // As Program.cs registers it: no HttpClient-wide timeout, DataverseClient times each request itself.
        Client = new DataverseClient(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
    }

    public Uri EnvironmentUrl { get; }

    public string Prefix { get; }

    public string AccessToken { get; }

    public DataverseClient Client { get; }

    public static async Task<LiveDataverse> ConnectAsync(CancellationToken cancellationToken)
    {
        var (environmentUrl, prefix) = Settings ?? throw new InvalidOperationException($"{EnvironmentVariable} and {PrefixVariable} must be set.");
        var loginHint = $"Run 'd365architect auth login --url {environmentUrl}' first.";

        AuthenticatedContext context;
        try
        {
            context = await new MsalAuthenticationService(AuthenticationOptions.FromEnvironment()).GetCurrentContextAsync(cancellationToken);
        }
        catch (AuthenticationRequiredException ex)
        {
            throw new InvalidOperationException($"{ex.Message} {loginHint}", ex);
        }

        // The cached session is for whichever environment was signed into last; its token is no good for another one.
        if (!string.Equals(context.EnvironmentUrl.Host, environmentUrl.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Signed in to {context.EnvironmentUrl}, not {environmentUrl}. {loginHint}");
        }

        return new LiveDataverse(environmentUrl, prefix, context.AccessToken);
    }

    /// <summary>A fresh lower-case name, <c>&lt;prefix&gt;_lt&lt;8 hex&gt;&lt;suffix&gt;</c>.</summary>
    public string NewName(string suffix = "") => $"{Prefix}_lt{Guid.NewGuid():N}"[..(Prefix.Length + 11)] + suffix;

    /// <summary>The table's MetadataId, or null when it doesn't exist.</summary>
    public async Task<Guid?> TryGetEntityMetadataIdAsync(string logicalName, CancellationToken cancellationToken)
    {
        var json = await GetAsync($"EntityDefinitions(LogicalName='{logicalName}')?$select=MetadataId", cancellationToken);
        return json is null ? null : Guid.Parse((string)json["MetadataId"]!);
    }

    /// <summary>Deletes the table; one that doesn't exist (never created, or rolled back) is fine.</summary>
    public Task DeleteTableAsync(string logicalName, CancellationToken cancellationToken) =>
        DeleteAsync($"EntityDefinitions(LogicalName='{logicalName}')", cancellationToken);

    /// <summary>Creates an unmanaged solution under the one publisher whose customisation prefix is <see cref="Prefix"/>; returns its id.</summary>
    public async Task<Guid> CreateSolutionAsync(string uniqueName, CancellationToken cancellationToken)
    {
        var publishers = (await GetAsync($"publishers?$select=publisherid&$filter=customizationprefix eq '{Prefix}'", cancellationToken))!["value"]!.AsArray();
        if (publishers.Count != 1)
        {
            throw new InvalidOperationException($"Expected one publisher with customisation prefix '{Prefix}' in {EnvironmentUrl}, found {publishers.Count}. Check {PrefixVariable}.");
        }

        using var request = Request(HttpMethod.Post, "solutions");
        request.Content = JsonContent.Create(new JsonObject
        {
            ["uniquename"] = uniqueName,
            ["friendlyname"] = $"Live test {uniqueName}",
            ["version"] = "1.0.0.0",
            ["publisherid@odata.bind"] = $"/publishers({(string)publishers[0]!["publisherid"]!})",
        });
        using var response = await _raw.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        // OData-EntityId: .../solutions(<guid>)
        var entityId = response.Headers.GetValues("OData-EntityId").Single();
        return Guid.Parse(entityId[(entityId.LastIndexOf('(') + 1)..^1]);
    }

    /// <summary>Deletes the solution (not its components); one that doesn't exist is fine.</summary>
    public Task DeleteSolutionAsync(Guid solutionId, CancellationToken cancellationToken) =>
        DeleteAsync($"solutions({solutionId})", cancellationToken);

    /// <summary>The objectids of the solution's components of <paramref name="componentType"/> (1 = table).</summary>
    public async Task<IReadOnlySet<Guid>> GetSolutionComponentIdsAsync(Guid solutionId, int componentType, CancellationToken cancellationToken)
    {
        var json = await GetAsync($"solutioncomponents?$select=objectid&$filter=_solutionid_value eq {solutionId} and componenttype eq {componentType}", cancellationToken);
        return json!["value"]!.AsArray().Select(c => Guid.Parse((string)c!["objectid"]!)).ToHashSet();
    }

    public void Dispose()
    {
        _raw.Dispose();
    }

    private async Task<JsonNode?> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, relativePath);
        using var response = await _raw.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private async Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Delete, relativePath);
        using var response = await _raw.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(EnvironmentUrl, "/api/data/v9.2/"), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }
    }
}

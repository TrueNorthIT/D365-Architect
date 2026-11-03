using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="DataverseClient"/>'s request bodies against the exact
/// live regression fixed by 4fc92be: <c>System.Net.Http.Json</c>'s
/// <c>JsonContent.Create(inputValue)</c> camelCases an anonymous object's
/// property names by default when no explicit <c>JsonSerializerOptions</c>
/// is given - not <c>JsonSerializerOptions.Default</c>'s own behaviour,
/// which preserves casing. Confirmed live: that silently rewrote
/// <c>PublishXmlAsync</c>'s <c>ParameterXml</c> to <c>parameterXml</c> on
/// the wire, which Dataverse rejected outright as an unrecognised
/// parameter. Both merged branches independently hit and fixed this same
/// bug, which is exactly the kind of regression worth locking in with a
/// test rather than trusting every future anonymous-object call site here
/// to remember it.
/// </summary>
public sealed class DataverseClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    private static (DataverseClient Client, CapturingHandler Handler) CreateClient()
    {
        var handler = new CapturingHandler();
        return (new DataverseClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task PublishEntityAsync_SendsParameterXmlInExactPascalCase()
    {
        var (client, handler) = CreateClient();

        await client.PublishEntityAsync(new Uri("https://example.crm.dynamics.com"), "token", "tn_test", CancellationToken.None);

        Assert.Contains("\"ParameterXml\":", handler.RequestBody);
        Assert.DoesNotContain("\"parameterXml\":", handler.RequestBody);
    }

    [Fact]
    public async Task UpdateSystemFormXmlAsync_SendsFormxmlUnchanged()
    {
        // formxml is already all-lowercase, so camelCasing it would have
        // been a silent no-op - this just confirms the fix didn't disturb
        // the one call site that happened to already be safe.
        var (client, handler) = CreateClient();

        await client.UpdateSystemFormXmlAsync(new Uri("https://example.crm.dynamics.com"), "token", Guid.NewGuid(), "<form></form>", CancellationToken.None);

        Assert.Contains("\"formxml\":", handler.RequestBody);
    }
}

/// <summary>
/// Covers a separate, later-found regression: <see cref="DataverseClient.UpdateGlobalOptionSetAsync"/>
/// is a full-object PUT structurally identical to <see cref="DataverseClient.UpdateEntityAsync"/>/
/// <see cref="DataverseClient.UpdateAttributeAsync"/> (same single-language
/// <c>DataverseLabelJson.Build</c> body), but never sent the
/// <c>MSCRM.MergeLabels</c> header those two send for exactly this reason —
/// confirmed live as a real gap: editing one language's label on a global
/// choice silently wiped every other language already set on it.
/// </summary>
public sealed class DataverseClientGlobalChoiceHeaderTests
{
    private sealed class HeaderCapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task UpdateGlobalOptionSetAsync_SendsMergeLabelsHeader()
    {
        var handler = new HeaderCapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.UpdateGlobalOptionSetAsync(new Uri("https://example.crm.dynamics.com"), "token", Guid.NewGuid(), new System.Text.Json.Nodes.JsonObject(), CancellationToken.None);

        Assert.True(handler.Request!.Headers.TryGetValues("MSCRM.MergeLabels", out var values));
        Assert.Equal("true", Assert.Single(values!));
    }
}

/// <summary>
/// Issue #35 (sub-issue of #30, "Solution import fixes"): without
/// <c>savedQueryId</c>, an ambiguous name still refuses, but the message
/// says to add <c>savedQueryId</c> and lists the candidate GUIDs. Driven
/// through <see cref="DataverseClient.TryGetSavedQueryAsync"/> against a
/// canned <c>savedqueries</c> response, since that is where the ambiguity is
/// detected and <see cref="AmbiguousSavedQueryException"/> is thrown.
/// </summary>
public sealed class DataverseClientAmbiguousSavedQueryTests
{
    private static readonly Guid FirstViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid SecondViewId = new("aaaaaaaa-0000-0000-0000-000000000001");

    private sealed class CannedResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static DataverseClient ClientReturning(string json) => new(new HttpClient(new CannedResponseHandler(json)));

    private static readonly string TwoViewsSharingANameJson = $$"""
        {
          "value": [
            { "savedqueryid": "{{FirstViewId}}", "description": null, "fetchxml": "<fetch/>", "layoutxml": "<grid/>" },
            { "savedqueryid": "{{SecondViewId}}", "description": null, "fetchxml": "<fetch/>", "layoutxml": "<grid/>" }
          ]
        }
        """;

    // Still refuses. Given two live views named 'My Inspections' on
    // tn_inspection, TryGetSavedQueryAsync throws
    // AmbiguousSavedQueryException instead of picking one.
    [Fact]
    public async Task TryGetSavedQueryAsync_TwoViewsShareTheName_StillThrowsAmbiguousSavedQueryException()
    {
        var client = ClientReturning(TwoViewsSharingANameJson);

        await Assert.ThrowsAsync<AmbiguousSavedQueryException>(() =>
            client.TryGetSavedQueryAsync(new Uri("https://example.crm.dynamics.com"), "token", "tn_inspection", "My Inspections", CancellationToken.None));
    }

    // The message tells the user how to fix it. It must name the
    // `savedQueryId` YAML key to add.
    [Fact]
    public async Task TryGetSavedQueryAsync_AmbiguousName_MessageTellsTheUserToAddSavedQueryId()
    {
        var client = ClientReturning(TwoViewsSharingANameJson);

        var ex = await Assert.ThrowsAsync<AmbiguousSavedQueryException>(() =>
            client.TryGetSavedQueryAsync(new Uri("https://example.crm.dynamics.com"), "token", "tn_inspection", "My Inspections", CancellationToken.None));

        Assert.True(ex.Message.Contains("savedQueryId", StringComparison.Ordinal),
            $"expected the ambiguity message to tell the user to add 'savedQueryId' to the view YAML, but it was: {ex.Message}");
    }

    // The message lists every candidate GUID so the user can pick
    // the right one without querying Dataverse themselves.
    [Fact]
    public async Task TryGetSavedQueryAsync_AmbiguousName_MessageListsEveryCandidateGuid()
    {
        var client = ClientReturning(TwoViewsSharingANameJson);

        var ex = await Assert.ThrowsAsync<AmbiguousSavedQueryException>(() =>
            client.TryGetSavedQueryAsync(new Uri("https://example.crm.dynamics.com"), "token", "tn_inspection", "My Inspections", CancellationToken.None));

        Assert.True(ex.Message.Contains(FirstViewId.ToString(), StringComparison.OrdinalIgnoreCase),
            $"expected the message to list candidate {FirstViewId}, but it was: {ex.Message}");
        Assert.True(ex.Message.Contains(SecondViewId.ToString(), StringComparison.OrdinalIgnoreCase),
            $"expected the message to list candidate {SecondViewId}, but it was: {ex.Message}");
    }
}

/// <summary>Issue #35, <see cref="DataverseClient.TryGetSavedQueryByIdAsync"/>'s request and its not-found handling.</summary>
public sealed class DataverseClientSavedQueryByIdTests
{
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");

    private sealed class StubHandler(System.Net.HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task TryGetSavedQueryByIdAsync_RequestsTheSavedQueryByKey()
    {
        var handler = new StubHandler(System.Net.HttpStatusCode.OK, $$"""{ "savedqueryid": "{{ViewId}}", "description": "d", "fetchxml": "<fetch/>", "layoutxml": "<grid/>" }""");
        var client = new DataverseClient(new HttpClient(handler));

        var result = await client.TryGetSavedQueryByIdAsync(new Uri("https://example.crm.dynamics.com"), "token", ViewId, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Contains($"savedqueries({ViewId})", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Contains("$select=savedqueryid,description,fetchxml,layoutxml", handler.Request.RequestUri.AbsoluteUri);
        Assert.Equal(new ExistingSavedQuery(ViewId, "d", "<fetch/>", "<grid/>"), result);
    }

    [Fact]
    public async Task TryGetSavedQueryByIdAsync_ReturnsTheLiveEntityAndName()
    {
        var handler = new StubHandler(System.Net.HttpStatusCode.OK, $$"""{ "savedqueryid": "{{ViewId}}", "returnedtypecode": "account", "name": "Active Accounts" }""");
        var client = new DataverseClient(new HttpClient(handler));

        var result = await client.TryGetSavedQueryByIdAsync(new Uri("https://example.crm.dynamics.com"), "token", ViewId, CancellationToken.None);

        Assert.Contains("returnedtypecode", handler.Request!.RequestUri!.AbsoluteUri);
        Assert.Equal("account", result!.EntityLogicalName);
        Assert.Equal("Active Accounts", result.Name);
    }

    [Fact]
    public async Task TryGetSavedQueryByIdAsync_NotFound_ReturnsNull()
    {
        var client = new DataverseClient(new HttpClient(new StubHandler(System.Net.HttpStatusCode.NotFound, "{}")));

        var result = await client.TryGetSavedQueryByIdAsync(new Uri("https://example.crm.dynamics.com"), "token", ViewId, CancellationToken.None);

        Assert.Null(result);
    }
}

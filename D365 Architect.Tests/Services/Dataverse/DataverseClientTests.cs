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

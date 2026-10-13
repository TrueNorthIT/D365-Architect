using System.Net;
using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers the <c>MSCRM.SolutionUniqueName</c> header this session added to
/// every create-a-new-component call (<see cref="DataverseClient.CreateAttributeAsync"/>/
/// <see cref="DataverseClient.CreateOneToManyRelationshipAsync"/>/
/// <see cref="DataverseClient.CreateGlobalOptionSetAsync"/>) — the fix for a
/// real, confirmed-live gap: without it, a brand-new column/relationship/
/// choice always landed wherever Dataverse's own default solution context
/// put it, never the solution whose YAML actually asked for it, so a
/// subsequent solution-scoped export silently wouldn't show it. Exercises
/// both paths a create can go through: an ordinary individual request, and
/// one bundled inside an <see cref="DataverseClient.ExecuteTransactionAsync"/>
/// changeset (which hand-builds its own raw HTTP text rather than using
/// <see cref="HttpRequestMessage.Headers"/>, so it needs its own assertion
/// shape) — see <see cref="DataverseClientTransactionTests"/> for the
/// changeset mechanics this reuses.
/// </summary>
public sealed class DataverseClientSolutionHeaderTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (DataverseClient Client, CapturingHandler Handler) CreateClient()
    {
        var handler = new CapturingHandler();
        return (new DataverseClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task CreateAttributeAsync_WithSolutionUniqueName_SendsTheHeader()
    {
        var (client, handler) = CreateClient();

        await client.CreateAttributeAsync(new Uri("https://example.crm.dynamics.com"), "token", "tn_test", new(), "JCTest", CancellationToken.None);

        Assert.True(handler.Request!.Headers.TryGetValues("MSCRM.SolutionUniqueName", out var values));
        Assert.Equal("JCTest", Assert.Single(values!));
    }

    [Fact]
    public async Task CreateAttributeAsync_WithoutSolutionUniqueName_SendsNoSuchHeader()
    {
        var (client, handler) = CreateClient();

        await client.CreateAttributeAsync(new Uri("https://example.crm.dynamics.com"), "token", "tn_test", new(), solutionUniqueName: null, CancellationToken.None);

        Assert.False(handler.Request!.Headers.Contains("MSCRM.SolutionUniqueName"));
    }

    [Fact]
    public async Task CreateOneToManyRelationshipAsync_WithSolutionUniqueName_SendsTheHeader()
    {
        var (client, handler) = CreateClient();

        await client.CreateOneToManyRelationshipAsync(new Uri("https://example.crm.dynamics.com"), "token", new(), "JCTest", CancellationToken.None);

        Assert.True(handler.Request!.Headers.TryGetValues("MSCRM.SolutionUniqueName", out var values));
        Assert.Equal("JCTest", Assert.Single(values!));
    }

    [Fact]
    public async Task CreateGlobalOptionSetAsync_WithSolutionUniqueName_SendsTheHeader()
    {
        var (client, handler) = CreateClient();

        await client.CreateGlobalOptionSetAsync(new Uri("https://example.crm.dynamics.com"), "token", new(), "JCTest", CancellationToken.None);

        Assert.True(handler.Request!.Headers.TryGetValues("MSCRM.SolutionUniqueName", out var values));
        Assert.Equal("JCTest", Assert.Single(values!));
    }

    // ---- The batched-changeset path: the header is hand-written into the
    // raw multipart request text, not set via HttpRequestMessage.Headers. ----

    private sealed class BatchCapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Join("\r\n",
                [
                    "--batchresponse_test",
                    "Content-Type: multipart/mixed; boundary=changesetresponse_test",
                    "",
                    "--changesetresponse_test",
                    "Content-Type: application/http",
                    "Content-Transfer-Encoding: binary",
                    "Content-ID: 1",
                    "",
                    "HTTP/1.1 204 No Content",
                    "",
                    "--changesetresponse_test--",
                    "--batchresponse_test--",
                ])),
            };
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/mixed; boundary=batchresponse_test");
            return response;
        }
    }

    [Fact]
    public async Task ExecuteTransactionAsync_CreateAttributeWriteWithSolutionUniqueName_IncludesTheHeaderInTheChangesetRequestText()
    {
        var handler = new BatchCapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.ExecuteTransactionAsync(new Uri("https://example.crm.dynamics.com"), "token",
            [new DataverseWrite.CreateAttribute("tn_test", new(), "JCTest")], CancellationToken.None);

        Assert.Contains("MSCRM.SolutionUniqueName: JCTest", handler.RequestBody);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_CreateAttributeWriteWithoutSolutionUniqueName_OmitsTheHeaderEntirely()
    {
        var handler = new BatchCapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.ExecuteTransactionAsync(new Uri("https://example.crm.dynamics.com"), "token",
            [new DataverseWrite.CreateAttribute("tn_test", new())], CancellationToken.None);

        Assert.DoesNotContain("MSCRM.SolutionUniqueName", handler.RequestBody);
    }
}

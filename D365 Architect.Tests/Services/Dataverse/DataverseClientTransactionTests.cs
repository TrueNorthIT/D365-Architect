using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="DataverseClient.ExecuteTransactionAsync"/> — the
/// batched-writes path <c>table import</c> uses by default (see
/// <see cref="Conversion.TableImportService"/>) instead of one Web API call
/// per column. Exercises the request this builds (CRLF-delimited
/// <c>multipart/mixed</c> text, one changeset wrapping one raw HTTP request
/// per write, in order) and how it reacts to the two response shapes
/// <see cref="ODataBatchResponseParserTests"/> already covers in isolation:
/// every write succeeding, and a changeset rollback.
/// </summary>
public sealed class DataverseClientTransactionTests
{
    /// <summary>Captures the outgoing request and returns a canned <c>$batch</c> response — no real HTTP call ever happens.</summary>
    private sealed class FakeBatchHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public int RequestCount { get; private set; }
        public string ResponseBody { get; set; } = "";
        public string ResponseBoundary { get; set; } = "batchresponse_test";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseBody) };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/mixed; boundary={ResponseBoundary}");
            return response;
        }
    }

    private static (DataverseClient Client, FakeBatchHandler Handler) CreateClient()
    {
        var handler = new FakeBatchHandler();
        return (new DataverseClient(new HttpClient(handler)), handler);
    }

    /// <summary>Builds a successful (all writes kept) batch response body for <paramref name="count"/> writes, mirroring Dataverse's own nested changesetresponse shape.</summary>
    private static string SuccessBody(int count)
    {
        var lines = new List<string> { "--batchresponse_test", "Content-Type: multipart/mixed; boundary=changesetresponse_test", "" };
        for (var i = 1; i <= count; i++)
        {
            lines.AddRange(["--changesetresponse_test", "Content-Type: application/http", "Content-Transfer-Encoding: binary", $"Content-ID: {i}", "", "HTTP/1.1 204 No Content", ""]);
        }

        lines.Add("--changesetresponse_test--");
        lines.Add("--batchresponse_test--");
        return string.Join("\r\n", lines);
    }

    private static string FailureBody(string errorMessage) => string.Join("\r\n",
        "--batchresponse_test",
        "Content-Type: application/http",
        "Content-Transfer-Encoding: binary",
        "",
        "HTTP/1.1 400 Bad Request",
        "Content-Type: application/json",
        "",
        $"{{\"error\":{{\"message\":\"{errorMessage}\"}}}}",
        "--batchresponse_test--");

    [Fact]
    public async Task ExecuteTransactionAsync_EmptyWrites_NeverSendsARequest()
    {
        var (client, handler) = CreateClient();

        await client.ExecuteTransactionAsync(new Uri("https://example.crm.dynamics.com"), "token", [], CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_BuildsOneChangesetPartPerWrite_WithCrlfLineEndings()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = SuccessBody(2);

        var writes = new List<DataverseWrite>
        {
            new DataverseWrite.CreateAttribute("tn_test", new JsonObject { ["SchemaName"] = "tn_New", ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata" }),
            new DataverseWrite.UpdateAttribute("tn_test", "tn_existing", new JsonObject { ["DisplayName"] = "renamed" }),
        };

        await client.ExecuteTransactionAsync(new Uri("https://example.crm.dynamics.com"), "token", writes, CancellationToken.None);

        Assert.NotNull(handler.RequestBody);
        var body = handler.RequestBody!;

        // Every line ending must be CRLF -- a bare LF anywhere is exactly the
        // kind of deserialization error Microsoft's own docs warn a $batch
        // request will hit.
        Assert.DoesNotMatch("(?<!\r)\n", body);

        Assert.Contains("Content-Type: multipart/mixed; boundary=\"changeset_", body);
        Assert.Contains("POST /api/data/v9.2/EntityDefinitions(LogicalName='tn_test')/Attributes HTTP/1.1", body);
        Assert.Contains("PUT /api/data/v9.2/EntityDefinitions(LogicalName='tn_test')/Attributes(LogicalName='tn_existing') HTTP/1.1", body);
        Assert.Contains("MSCRM.MergeLabels: true", body);
        Assert.Contains("\"SchemaName\":\"tn_New\"", body);
        Assert.Contains("Content-ID: 1", body);
        Assert.Contains("Content-ID: 2", body);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_EverySubRequestSucceeds_DoesNotThrow()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = SuccessBody(1);

        await client.ExecuteTransactionAsync(new Uri("https://example.crm.dynamics.com"), "token",
            [new DataverseWrite.CreateAttribute("tn_test", new JsonObject { ["SchemaName"] = "tn_New" })], CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_ChangesetRolledBack_ThrowsWithDataversesOwnErrorMessage()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = FailureBody("SchemaName already in use");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.ExecuteTransactionAsync(
            new Uri("https://example.crm.dynamics.com"), "token",
            [new DataverseWrite.CreateAttribute("tn_test", new JsonObject { ["SchemaName"] = "tn_Dup" })], CancellationToken.None));

        Assert.Contains("SchemaName already in use", ex.Message);
        Assert.Contains("rolled back", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

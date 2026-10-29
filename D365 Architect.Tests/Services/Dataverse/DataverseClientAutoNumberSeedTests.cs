using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// How <see cref="DataverseClient"/> puts the seed write (REQ-20) on the
/// wire. Shape from Microsoft Learn, "Create autonumber columns", "Set a seed
/// value" (the unbound <c>SetAutoNumberSeed</c> action; parameters
/// <c>EntityName</c> and <c>AttributeName</c> as logical names and <c>Value</c>
/// as an int, "the next autonumber value for the column"):
/// https://learn.microsoft.com/power-apps/developer/data-platform/create-auto-number-attributes
/// Both the individual request and the batched-changeset path are covered, as
/// in <see cref="DataverseClientTableCreateTests"/>.
///
/// Not confirmed live: that Dataverse accepts <c>SetAutoNumberSeed</c> inside
/// the same <c>$batch</c> changeset as the create of the column it seeds.
/// </summary>
public sealed class DataverseClientAutoNumberSeedTests
{
    private static readonly Uri EnvironmentUrl = new("https://example.crm.dynamics.com");

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public string BatchResponseBody { get; set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (request.RequestUri!.AbsolutePath.EndsWith("/$batch", StringComparison.Ordinal))
            {
                var batch = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BatchResponseBody) };
                batch.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=batchresponse_test");
                return batch;
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

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

    // REQ-20: the individual request is POST SetAutoNumberSeed { EntityName, AttributeName, Value } (Value a JSON number).
    // Code path: DataverseClient.SetAutoNumberSeedAsync -> ToHttpRequest(DataverseWrite.SetAutoNumberSeed).
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task SetAutoNumberSeedAsync_PostsTheSeedActionWithEntityAttributeAndValue()
    {
        var handler = new CapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.SetAutoNumberSeedAsync(EnvironmentUrl, "token", "tn_test", "bogdan_ref", 1, CancellationToken.None);

        Expect.Equal("POST", handler.Request!.Method.Method, "REQ-20: setting the seed should be a POST (an unbound action).");
        Expect.True(handler.Request.RequestUri!.AbsolutePath.EndsWith("/api/data/v9.2/SetAutoNumberSeed", StringComparison.Ordinal),
            $"REQ-20: the seed should POST to the SetAutoNumberSeed action. Actual path: <{handler.Request.RequestUri.AbsolutePath}>.");
        var body = JsonNode.Parse(handler.RequestBody!)!.AsObject();
        Expect.Equal("tn_test", (string?)body["EntityName"], "REQ-20: EntityName should be the table's logical name.");
        Expect.Equal("bogdan_ref", (string?)body["AttributeName"], "REQ-20: AttributeName should be the column's logical name.");
        Expect.Equal(1, (int?)body["Value"], "REQ-20: Value should be the seed as a number.");
    }

    // REQ-20: the same write inside a $batch changeset (the default `table import` path) is a POST of the same action, after the create.
    // Code path: DataverseClient.ExecuteTransactionAsync -> BuildTransactionRequestBody -> ToHttpRequest.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ExecuteTransactionAsync_SeedWrite_IsAPostOfTheSeedActionAfterTheCreate()
    {
        var handler = new CapturingHandler { BatchResponseBody = SuccessBody(2) };
        var client = new DataverseClient(new HttpClient(handler));
        var writes = new List<DataverseWrite>
        {
            new DataverseWrite.CreateAttribute("tn_test", new JsonObject { ["SchemaName"] = "bogdan_Ref" }),
            new DataverseWrite.SetAutoNumberSeed("tn_test", "bogdan_ref", 1),
        };

        await client.ExecuteTransactionAsync(EnvironmentUrl, "token", writes, CancellationToken.None);

        var body = handler.RequestBody!;
        var seedAt = body.IndexOf("POST /api/data/v9.2/SetAutoNumberSeed HTTP/1.1", StringComparison.Ordinal);
        var createAt = body.IndexOf("POST /api/data/v9.2/EntityDefinitions(LogicalName='tn_test')/Attributes HTTP/1.1", StringComparison.Ordinal);
        Expect.True(seedAt >= 0, "REQ-20: the changeset should contain 'POST /api/data/v9.2/SetAutoNumberSeed HTTP/1.1'.");
        Expect.True(createAt >= 0 && createAt < seedAt, "REQ-20: the seed request should come after the column's create in the changeset.");
        Expect.Contains("\"AttributeName\":\"bogdan_ref\"", body, "REQ-20: the seed request body should name the column.");
        Expect.Contains("\"Value\":1", body, "REQ-20: the seed request body should carry the value as a number.");
    }

    // REQ-20: a rolled-back changeset is reported by describing each write; the seed write must name its column.
    // Code path: DataverseWrite.Describe.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public void Describe_SetAutoNumberSeed_NamesTheColumn()
    {
        var description = new DataverseWrite.SetAutoNumberSeed("tn_test", "bogdan_ref", 1).Describe();

        Expect.Contains("bogdan_ref", description, "REQ-20: the description of a seed write should name the column, like the other writes' descriptions do.");
    }
}

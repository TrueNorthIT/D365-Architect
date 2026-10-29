using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// <see cref="DataverseClient.ExecuteTransactionAsync"/> as <c>solution import</c> uses it to create
/// several tables at once (the "create tables in one transaction" behaviour, tagged
/// with the "Feature" trait "batch-table-create"): one <c>$batch</c>
/// request holding one changeset of <c>POST EntityDefinitions</c> requests, the solution header on each,
/// a rolled-back changeset naming the failing request by its <c>Content-ID</c>, and a changeset timeout
/// longer than an ordinary request's (each table create takes 14 to 40 seconds).
///
/// Live verification of the metadata batch against a real tenant happens separately; these pin what this
/// client sends and how it reads Dataverse's documented response shapes.
/// </summary>
public sealed class DataverseClientBatchCreateTests
{
    private static readonly Uri EnvironmentUrl = new("https://example.crm.dynamics.com");

    private sealed class BatchHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public int RequestCount { get; private set; }
        public string ResponseBody { get; set; } = "";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return BatchResponse(ResponseBody, StatusCode);
        }
    }

    private static HttpResponseMessage BatchResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=batchresponse_test");
        return response;
    }

    private static string Join(params string[] lines) => string.Join("\r\n", lines);

    private static string SuccessBody(int count)
    {
        var lines = new List<string> { "--batchresponse_test", "Content-Type: multipart/mixed; boundary=changesetresponse_test", "" };
        for (var i = 1; i <= count; i++)
        {
            lines.AddRange(["--changesetresponse_test", "Content-Type: application/http", "Content-Transfer-Encoding: binary", $"Content-ID: {i}", "", "HTTP/1.1 204 No Content", ""]);
        }

        lines.Add("--changesetresponse_test--");
        lines.Add("--batchresponse_test--");
        return Join([.. lines]);
    }

    /// <summary>Dataverse's documented shape for a rolled-back changeset: one flat error part under the outer boundary, carrying the Content-ID of the request that failed.</summary>
    private static string RolledBackBody(string? contentId, string message) => Join(
        "--batchresponse_test",
        "Content-Type: application/http",
        "Content-Transfer-Encoding: binary",
        contentId is null ? "X-NoContentId: true" : $"Content-ID: {contentId}",
        "",
        "HTTP/1.1 400 Bad Request",
        "Content-Type: application/json; odata.metadata=minimal",
        "",
        $"{{\"error\":{{\"code\":\"0x80044331\",\"message\":\"{message}\"}}}}",
        "--batchresponse_test--");

    private static DataverseWrite.CreateEntity Create(string schemaName, string? solution = "TNCore") =>
        new(new JsonObject { ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata", ["SchemaName"] = schemaName }, solution);

    private static IReadOnlyList<DataverseWrite> ThreeCreates() => [Create("tn_Project"), Create("tn_Task"), Create("tn_Note")];

    // ---- the request ----

    // "a single Web API $batch changeset of POST EntityDefinitions requests" - one HTTP request, one changeset, a POST per table numbered 1..3.
    // Code path: DataverseClient.ExecuteTransactionAsync -> BuildTransactionRequestBody with DataverseWrite.CreateEntity. (Guard: already true of the existing batch builder.)
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ExecuteTransactionAsync_ThreeCreateEntityWrites_AreOneBatchRequestWithOneChangesetOfThreePosts()
    {
        var handler = new BatchHandler { ResponseBody = SuccessBody(3) };
        var client = new DataverseClient(new HttpClient(handler));

        await client.ExecuteTransactionAsync(EnvironmentUrl, "token", ThreeCreates(), CancellationToken.None);

        var body = handler.RequestBody ?? "";
        Expect.Equal(1, handler.RequestCount, "all the creates should go in one HTTP request.");
        Expect.Equal(1, System.Text.RegularExpressions.Regex.Matches(body, "Content-Type: multipart/mixed; boundary=\"changeset_").Count, "the requests should be wrapped in exactly one changeset.");
        Expect.Equal(3, System.Text.RegularExpressions.Regex.Matches(body, "POST /api/data/v9.2/EntityDefinitions HTTP/1.1").Count, "there should be one POST EntityDefinitions per table.");
        foreach (var id in new[] { 1, 2, 3 })
        {
            Expect.Contains($"Content-ID: {id}", body, $"request {id} should carry Content-ID {id}, so a failure can be mapped back to the table.");
        }
    }

    // "Tables created together are still added to `--solution`" - the MSCRM.SolutionUniqueName header on each request inside the changeset. (Guard.)
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ExecuteTransactionAsync_CreateEntityWritesWithSolution_SendTheHeaderOnEveryRequest()
    {
        var handler = new BatchHandler { ResponseBody = SuccessBody(3) };
        var client = new DataverseClient(new HttpClient(handler));

        await client.ExecuteTransactionAsync(EnvironmentUrl, "token", ThreeCreates(), CancellationToken.None);

        var count = System.Text.RegularExpressions.Regex.Matches(handler.RequestBody ?? "", "MSCRM.SolutionUniqueName: TNCore").Count;
        Expect.Equal(3, count, "each of the three requests in the changeset should carry MSCRM.SolutionUniqueName: TNCore.");
    }

    // ---- reading a rolled-back changeset ----

    // "Dataverse's batch error identifies the failing request by Content-ID - map it back to the table."
    // Code path: ODataBatchResponseParser (Part.ContentId) -> DataverseClient.ExecuteTransactionAsync throws DataverseTransactionFailedException(FailedContentId).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ExecuteTransactionAsync_RolledBackNamingRequestTwo_ThrowsWithThatContentId()
    {
        var handler = new BatchHandler { ResponseBody = RolledBackBody("2", "SchemaName tn_Task already in use") };
        var client = new DataverseClient(new HttpClient(handler));

        var ex = await Capture(() => client.ExecuteTransactionAsync(EnvironmentUrl, "token", ThreeCreates(), CancellationToken.None));

        var failure = ex as DataverseTransactionFailedException;
        Expect.True(failure is not null, $"a rolled-back changeset should throw DataverseTransactionFailedException. Actual: <{ex?.GetType().Name ?? "nothing"}>.");
        Expect.Equal(2, failure!.FailedContentId, "the exception should carry the Content-ID of the request Dataverse said failed.");
        Expect.Contains("SchemaName tn_Task already in use", failure.Message, "the exception message should keep Dataverse's own error.");
        Expect.Contains("rolled back", failure.Message, "the exception message should say the transaction was rolled back (nothing was applied).");
    }

    // A rolled-back changeset as live Dataverse actually answers it (captured from a live test run): the whole $batch comes back 400, with the
    // error part nested inside a changesetresponse. The Content-ID must still be read rather than the 400 thrown as-is.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ExecuteTransactionAsync_LiveShaped400WithNestedChangeset_ThrowsWithThatContentId()
    {
        var handler = new BatchHandler
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = Join(
                "--batchresponse_test",
                "Content-Type: multipart/mixed; boundary=changesetresponse_test",
                "",
                "--changesetresponse_test",
                "Content-Type: application/http",
                "Content-Transfer-Encoding: binary",
                "Content-ID: 3",
                "",
                "HTTP/1.1 400 Bad Request",
                "REQ_ID: 69c86e66-3481-4f20-a650-e4acec0f1eab",
                "Content-Type: application/json; odata.metadata=minimal",
                "OData-Version: 4.0",
                "",
                "{\"error\":{\"code\":\"0x8009050d\",\"message\":\"Entities already exist: tn_project.\"}}",
                "--changesetresponse_test--",
                "--batchresponse_test--"),
        };
        var client = new DataverseClient(new HttpClient(handler));

        var ex = await Capture(() => client.ExecuteTransactionAsync(EnvironmentUrl, "token", ThreeCreates(), CancellationToken.None));

        var failure = ex as DataverseTransactionFailedException;
        Expect.True(failure is not null, $"a 400 $batch response carrying a failed changeset should throw DataverseTransactionFailedException. Actual: <{ex?.GetType().Name ?? "nothing"}>.");
        Expect.Equal(3, failure!.FailedContentId, "the Content-ID nested in the changeset response should be read.");
        Expect.Contains("Entities already exist", failure.Message, "the exception message should keep Dataverse's own error.");
    }

    // When the failure carries no Content-ID, it is still reported as a rollback - the id is just unknown, never guessed.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ExecuteTransactionAsync_RolledBackWithNoContentId_ThrowsWithAnUnknownContentId()
    {
        var handler = new BatchHandler { ResponseBody = RolledBackBody(null, "boom") };
        var client = new DataverseClient(new HttpClient(handler));

        var ex = await Capture(() => client.ExecuteTransactionAsync(EnvironmentUrl, "token", ThreeCreates(), CancellationToken.None));

        var failure = ex as DataverseTransactionFailedException;
        Expect.True(failure is not null, $"a rolled-back changeset should throw DataverseTransactionFailedException even with no Content-ID. Actual: <{ex?.GetType().Name ?? "nothing"}>.");
        Expect.Equal(null, failure!.FailedContentId, "with no Content-ID in the response the failing request should be reported as unknown, not guessed.");
    }

    // The parser's half of the above: the Content-ID header of the failed (flat) part, and of each leaf of a successful changeset.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public void ParseParts_FailedChangeset_ReportsTheContentIdOfTheFailingRequest()
    {
        var parts = ODataBatchResponseParser.ParseParts(RolledBackBody("2", "boom"), "batchresponse_test");

        var part = Assert.Single(parts);
        Expect.Equal("2", part.ContentId, "the error part's own Content-ID header should be read into Part.ContentId.");
    }

    [Fact]
    [Trait("Feature", "batch-table-create")]
    public void ParseParts_SuccessfulChangeset_ReportsEachLeafsContentId()
    {
        var parts = ODataBatchResponseParser.ParseParts(SuccessBody(2), "batchresponse_test");

        Expect.Sequence(["1", "2"], parts.Select(p => p.ContentId ?? "null").ToList(), "each leaf of a successful changeset should report its Content-ID.");
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // ---- the timeout ----

    // "The changeset request must use a longer HTTP timeout than normal requests (table creates take 14-40s each)" - by default it is longer than HttpClient's own.
    // Code path: DataverseClient.TransactionTimeout (default), applied by ExecuteTransactionAsync.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public void TransactionTimeout_DefaultIsLongerThanAnOrdinaryRequests()
    {
        var client = new DataverseClient(new HttpClient(new BatchHandler()));

        Expect.True(client.TransactionTimeout > new HttpClient().Timeout,
            $"A changeset of table creates should be allowed longer than an ordinary request (HttpClient's default is {new HttpClient().Timeout}). Actual TransactionTimeout: <{client.TransactionTimeout}>.");
    }

    /// <summary>Waits (up to a safety limit) for the token it is given to be cancelled, then answers as a successful batch - so a client that never times out still finishes the test.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        public bool TokenWasCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TokenWasCancelled = true;
                throw;
            }

            return BatchResponse(SuccessBody(1));
        }
    }

    // ... and it is the configured value, not a constant: with a short TransactionTimeout the request is cancelled once that long has passed.
    // Code path: DataverseClient.ExecuteTransactionAsync applying TransactionTimeout to the $batch request (HttpClient.Timeout alone can't be lengthened per request,
    // so the typed client registered in Program.cs also has to stop its own default 100 s timeout cutting a changeset short).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ExecuteTransactionAsync_UsesTheConfiguredTimeout()
    {
        var handler = new HangingHandler();
        var client = new DataverseClient(new HttpClient(handler)) { TransactionTimeout = TimeSpan.FromMilliseconds(100) };

        await Capture(() => client.ExecuteTransactionAsync(EnvironmentUrl, "token", [Create("tn_Project")], CancellationToken.None));

        Expect.True(handler.TokenWasCancelled, "The $batch request should be cancelled once TransactionTimeout (100 ms here) has passed; it was still running after 3 s.");
    }
}

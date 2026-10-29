using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using D365Architect.Services.Dataverse;
using Xunit;
using D365Architect.Tests.TestSupport;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers how <see cref="DataverseClient"/> maps the new "create table"
/// write onto the wire: <c>POST EntityDefinitions</c> with the
/// <c>MSCRM.SolutionUniqueName</c> header, per Microsoft Learn, "Create table
/// definitions" (the request line, the header and the body are all in its
/// example): https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api
/// Also the existence check <c>table create</c> makes first, and the
/// <c>PrimaryNameAttribute</c> the table read must now ask for.
/// Both the individual request and the batched-changeset path are covered, as
/// in <see cref="DataverseClientSolutionHeaderTests"/>.
/// </summary>
public sealed class DataverseClientTableCreateTests
{
    private static readonly Uri EnvironmentUrl = new("https://example.crm.dynamics.com");

    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.NoContent) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent("{\"error\":{\"message\":\"nope\"}}") };
        }
    }

    private static JsonObject SomeTableBody() => new() { ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata", ["SchemaName"] = "tn_Project" };

    // ---- POST EntityDefinitions ----

    // "creates a table" - the individual request is a POST to the EntityDefinitions collection, body unchanged.
    // Code path: DataverseClient.CreateEntityAsync -> ToHttpRequest(DataverseWrite.CreateEntity).
    [Fact]
    public async Task CreateEntityAsync_PostsTheBodyToEntityDefinitions()
    {
        var handler = new CapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.CreateEntityAsync(EnvironmentUrl, "token", SomeTableBody(), solutionUniqueName: null, CancellationToken.None);

        Expect.Equal("POST", handler.Request!.Method.Method, "creating a table should be a POST.");
        Expect.True(handler.Request.RequestUri!.AbsolutePath.EndsWith("/api/data/v9.2/EntityDefinitions", StringComparison.Ordinal),
            $"creating a table should POST to the EntityDefinitions collection (no key segment). Actual path: <{handler.Request.RequestUri.AbsolutePath}>.");
        Expect.Contains("\"SchemaName\":\"tn_Project\"", handler.RequestBody, "the EntityMetadata body should be sent as given.");
    }

    // A rejected create surfaces as an error (what TableCreateService turns into 'Creating table ... failed').
    [Fact]
    public async Task CreateEntityAsync_DataverseRejects_Throws()
    {
        var client = new DataverseClient(new HttpClient(new CapturingHandler(HttpStatusCode.BadRequest)));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateEntityAsync(EnvironmentUrl, "token", SomeTableBody(), "TNCore", CancellationToken.None));
    }

    // ---- solution header ----

    // "`--solution` adds the new table to that solution as it's created" - MSCRM.SolutionUniqueName on the POST.
    [Fact]
    public async Task CreateEntityAsync_WithSolutionUniqueName_SendsTheHeader()
    {
        var handler = new CapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.CreateEntityAsync(EnvironmentUrl, "token", SomeTableBody(), "TNCore", CancellationToken.None);

        Expect.True(handler.Request!.Headers.TryGetValues("MSCRM.SolutionUniqueName", out var values), "the create request should carry the MSCRM.SolutionUniqueName header.");
        Expect.Equal("TNCore", Assert.Single(values!), "the header should name the solution given.");
    }

    [Fact]
    public async Task CreateEntityAsync_WithoutSolutionUniqueName_SendsNoSuchHeader()
    {
        var handler = new CapturingHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.CreateEntityAsync(EnvironmentUrl, "token", SomeTableBody(), solutionUniqueName: null, CancellationToken.None);

        Expect.Equal(false, handler.Request!.Headers.Contains("MSCRM.SolutionUniqueName"), "with no solution given, no MSCRM.SolutionUniqueName header should be sent.");
    }

    // The batched path builds its own request text, so it needs its own assertion (see DataverseClientSolutionHeaderTests).
    private sealed class BatchHandler : HttpMessageHandler
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
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=batchresponse_test");
            return response;
        }
    }

    [Fact]
    public async Task ExecuteTransactionAsync_CreateEntityWriteWithSolution_PostsToEntityDefinitionsWithTheHeader()
    {
        var handler = new BatchHandler();
        var client = new DataverseClient(new HttpClient(handler));

        await client.ExecuteTransactionAsync(EnvironmentUrl, "token", [new DataverseWrite.CreateEntity(SomeTableBody(), "TNCore")], CancellationToken.None);

        Expect.Contains("POST /api/data/v9.2/EntityDefinitions HTTP/1.1", handler.RequestBody, "a CreateEntity write in a changeset should be a POST to EntityDefinitions.");
        Expect.Contains("MSCRM.SolutionUniqueName: TNCore", handler.RequestBody, "a CreateEntity write in a changeset should carry the solution header.");
    }

    // ---- existence check ----

    // "If the table already exists, the command says so" - needs a reliable yes/no.
    // Code path: DataverseClient.EntityExistsAsync (GET EntityDefinitions(LogicalName='...')): 200 = true, 404 = false.
    [Fact]
    public async Task EntityExistsAsync_Found_ReturnsTrueAndLooksUpByLogicalName()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var client = new DataverseClient(new HttpClient(handler));

        var exists = await client.EntityExistsAsync(EnvironmentUrl, "token", "tn_project", CancellationToken.None);

        Expect.Equal(true, exists, "a table Dataverse returns should be reported as existing.");
        Expect.Contains("EntityDefinitions(LogicalName='tn_project')", handler.Request!.RequestUri!.AbsoluteUri, "the existence check should look the table up by its logical name.");
    }

    [Fact]
    public async Task EntityExistsAsync_NotFound_ReturnsFalse()
    {
        var client = new DataverseClient(new HttpClient(new CapturingHandler(HttpStatusCode.NotFound)));

        var exists = await client.EntityExistsAsync(EnvironmentUrl, "token", "tn_project", CancellationToken.None);

        Expect.Equal(false, exists, "a 404 for the table means it can be created.");
    }

    // Anything other than found / not found must not be mistaken for "doesn't exist" - that would POST a create against a table that may be there.
    [Fact]
    public async Task EntityExistsAsync_ServerError_Throws()
    {
        var client = new DataverseClient(new HttpClient(new CapturingHandler(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.EntityExistsAsync(EnvironmentUrl, "token", "tn_project", CancellationToken.None));
    }

    // ---- the table read asks for PrimaryNameAttribute ----

    // "`table export` writes `primaryNameAttribute`" - the live read's $select has to name it, or the reader never sees it.
    // Code path: DataverseClient.GetEntityDefinitionJsonAsync.
    [Fact]
    public async Task GetEntityDefinitionJsonAsync_SelectsPrimaryNameAttribute()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK);
        var client = new DataverseClient(new HttpClient(handler));

        await client.GetEntityDefinitionJsonAsync(EnvironmentUrl, "token", "account", CancellationToken.None);

        Expect.Contains("PrimaryNameAttribute", handler.Request!.RequestUri!.AbsoluteUri, "the table read's $select should include PrimaryNameAttribute so export can write it.");
    }
}

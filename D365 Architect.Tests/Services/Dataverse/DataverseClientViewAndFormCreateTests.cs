using System.Net;
using System.Text.Json.Nodes;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// How <see cref="DataverseClient"/> maps the new view and form writes and
/// reads onto the wire:
/// <list type="bullet">
/// <item><c>POST savedqueries</c> with <c>MSCRM.SolutionUniqueName</c> - Learn, "Customize Views in Model-driven Apps" (Create views) and "Use optional parameters":
/// https://learn.microsoft.com/power-apps/developer/model-driven-apps/customize-entity-views ,
/// https://learn.microsoft.com/power-apps/developer/data-platform/optional-parameters#associate-a-solution-component-with-a-solution</item>
/// <item><c>POST systemforms</c> (the SystemForm Create message): https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/systemform</item>
/// <item>the same header on <c>PATCH</c> for an existing view or form - Learn lists Update (PATCH) among the operations that accept it, so no separate AddSolutionComponent call (component types 26 and 60) is needed</item>
/// <item><c>GET EntityDefinitions(LogicalName='...')?$select=ObjectTypeCode,PrimaryIdAttribute,PrimaryNameAttribute</c> - EntityMetadata properties per Learn: https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/entitymetadata</item>
/// </list>
/// </summary>
public sealed class DataverseClientViewAndFormCreateTests
{
    private static readonly Uri EnvironmentUrl = new("https://example.crm.dynamics.com");

    private sealed class CannedHandler(HttpStatusCode status = HttpStatusCode.NoContent, string body = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (DataverseClient Client, CannedHandler Handler) Client(HttpStatusCode status = HttpStatusCode.NoContent, string body = "{}")
    {
        var handler = new CannedHandler(status, body);
        return (new DataverseClient(new HttpClient(handler)), handler);
    }

    private static string? Header(CannedHandler handler) =>
        handler.Request!.Headers.TryGetValues("MSCRM.SolutionUniqueName", out var values) ? Assert.Single(values) : null;

    private static string Decoded(CannedHandler handler) => Uri.UnescapeDataString(handler.Request!.RequestUri!.PathAndQuery);

    // ---- create a view ----

    // POST to the savedqueries collection, body as given.
    [Fact]
    public async Task CreateSavedQueryAsync_PostsTheBodyToSavedQueries()
    {
        var (client, handler) = Client();

        await client.CreateSavedQueryAsync(EnvironmentUrl, "token", new JsonObject { ["name"] = "All Invoice Centres", ["querytype"] = 0 }, null, CancellationToken.None);

        Expect.Equal("POST", handler.Request!.Method.Method, "creating a view should be a POST.");
        Expect.True(handler.Request.RequestUri!.AbsolutePath.EndsWith("/api/data/v9.2/savedqueries", StringComparison.Ordinal), $"a view should be POSTed to the savedqueries collection. Actual path: <{handler.Request.RequestUri.AbsolutePath}>.");
        Expect.Contains("\"name\":\"All Invoice Centres\"", handler.RequestBody, "the body should be sent as given.");
        Expect.Contains("\"querytype\":0", handler.RequestBody, "querytype should be the number 0.");
    }

    // The header on the create.
    [Fact]
    public async Task CreateSavedQueryAsync_WithSolution_SendsTheSolutionHeader()
    {
        var (client, handler) = Client();

        await client.CreateSavedQueryAsync(EnvironmentUrl, "token", new JsonObject(), "TableCreateTest", CancellationToken.None);

        Expect.Equal("TableCreateTest", Header(handler), "the create should carry MSCRM.SolutionUniqueName naming the solution.");
    }

    [Fact]
    public async Task CreateSavedQueryAsync_WithoutSolution_SendsNoSolutionHeader()
    {
        var (client, handler) = Client();

        await client.CreateSavedQueryAsync(EnvironmentUrl, "token", new JsonObject(), null, CancellationToken.None);

        Expect.Equal(null, Header(handler), "with no solution, no MSCRM.SolutionUniqueName header should be sent.");
    }

    // ---- create a form ----

    [Fact]
    public async Task CreateSystemFormAsync_PostsTheBodyToSystemForms()
    {
        var (client, handler) = Client();

        await client.CreateSystemFormAsync(EnvironmentUrl, "token", new JsonObject { ["name"] = "Invoice Centre", ["type"] = 2, ["formxml"] = "<form/>" }, null, CancellationToken.None);

        Expect.Equal("POST", handler.Request!.Method.Method, "creating a form should be a POST.");
        Expect.True(handler.Request.RequestUri!.AbsolutePath.EndsWith("/api/data/v9.2/systemforms", StringComparison.Ordinal), $"a form should be POSTed to the systemforms collection. Actual path: <{handler.Request.RequestUri.AbsolutePath}>.");
        var sent = JsonNode.Parse(handler.RequestBody!)!;
        Expect.Equal("<form/>", (string?)sent["formxml"], "formxml should be sent as given.");
        Expect.Equal(2, (int?)sent["type"], "type should be the option-set integer.");
    }

    [Fact]
    public async Task CreateSystemFormAsync_WithSolution_SendsTheSolutionHeader()
    {
        var (client, handler) = Client();

        await client.CreateSystemFormAsync(EnvironmentUrl, "token", new JsonObject(), "TableCreateTest", CancellationToken.None);

        Expect.Equal("TableCreateTest", Header(handler), "the create should carry MSCRM.SolutionUniqueName naming the solution.");
    }

    [Fact]
    public async Task CreateSystemFormAsync_WithoutSolution_SendsNoSolutionHeader()
    {
        var (client, handler) = Client();

        await client.CreateSystemFormAsync(EnvironmentUrl, "token", new JsonObject(), null, CancellationToken.None);

        Expect.Equal(null, Header(handler), "with no solution, no MSCRM.SolutionUniqueName header should be sent.");
    }

    // ---- update an existing view / form into a solution ----

    [Fact]
    public async Task UpdateSavedQueryAsync_WithSolution_PatchesTheViewWithTheSolutionHeader()
    {
        var (client, handler) = Client();
        var id = Guid.NewGuid();

        await client.UpdateSavedQueryAsync(EnvironmentUrl, "token", id, null, "<fetch/>", null, "TableCreateTest", CancellationToken.None);

        Expect.Equal("PATCH", handler.Request!.Method.Method, "updating a view should be a PATCH.");
        Expect.Contains($"savedqueries({id})", handler.Request.RequestUri!.AbsolutePath, "the PATCH should target the view by id.");
        Expect.Equal("TableCreateTest", Header(handler), "the PATCH should carry MSCRM.SolutionUniqueName so the changed view joins the solution.");
        Expect.Contains("\"fetchxml\"", handler.RequestBody, "the change itself should still be sent.");
    }

    [Fact]
    public async Task UpdateSavedQueryAsync_WithoutSolution_SendsNoSolutionHeader()
    {
        var (client, handler) = Client();

        await client.UpdateSavedQueryAsync(EnvironmentUrl, "token", Guid.NewGuid(), null, "<fetch/>", null, solutionUniqueName: null, CancellationToken.None);

        Expect.Equal(null, Header(handler), "with no solution, no MSCRM.SolutionUniqueName header should be sent.");
    }

    [Fact]
    public async Task UpdateSystemFormXmlAsync_WithSolution_PatchesTheFormWithTheSolutionHeader()
    {
        var (client, handler) = Client();
        var id = Guid.NewGuid();

        await client.UpdateSystemFormXmlAsync(EnvironmentUrl, "token", id, "<form/>", "TableCreateTest", CancellationToken.None);

        Expect.Equal("PATCH", handler.Request!.Method.Method, "updating a form should be a PATCH.");
        Expect.Contains($"systemforms({id})", handler.Request.RequestUri!.AbsolutePath, "the PATCH should target the form by id.");
        Expect.Equal("TableCreateTest", Header(handler), "the PATCH should carry MSCRM.SolutionUniqueName so the changed form joins the solution.");
    }

    [Fact]
    public async Task UpdateSystemFormXmlAsync_WithoutSolution_SendsNoSolutionHeader()
    {
        var (client, handler) = Client();

        await client.UpdateSystemFormXmlAsync(EnvironmentUrl, "token", Guid.NewGuid(), "<form/>", solutionUniqueName: null, CancellationToken.None);

        Expect.Equal(null, Header(handler), "with no solution, no MSCRM.SolutionUniqueName header should be sent.");
    }

    // ---- the table's identity ----

    [Fact]
    public async Task TryGetEntityIdentityAsync_Found_ReturnsTypeCodeIdAndNameAttributes()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """{"ObjectTypeCode":10722,"PrimaryIdAttribute":"bogdan_tablecreatetestid","PrimaryNameAttribute":"bogdan_name","MetadataId":"00000000-0000-0000-0000-000000000001"}""");

        var identity = await client.TryGetEntityIdentityAsync(EnvironmentUrl, "token", "bogdan_tablecreatetest", CancellationToken.None);

        Expect.Equal(new EntityIdentity(10722, "bogdan_tablecreatetestid", "bogdan_name"), identity, "the type code and the primary id and name attributes should be read from the table's metadata.");
        var path = Decoded(handler);
        Expect.Contains("EntityDefinitions(LogicalName='bogdan_tablecreatetest')", path, "the table should be looked up by its logical name.");
        foreach (var property in new[] { "ObjectTypeCode", "PrimaryIdAttribute", "PrimaryNameAttribute" })
        {
            Expect.Contains(property, path, $"the request should $select {property}.");
        }
    }

    // A 404 means "no such table", not an error: the caller turns it into a clear refusal.
    [Fact]
    public async Task TryGetEntityIdentityAsync_NotFound_ReturnsNull()
    {
        var (client, _) = Client(HttpStatusCode.NotFound);

        var identity = await client.TryGetEntityIdentityAsync(EnvironmentUrl, "token", "bogdan_nope", CancellationToken.None);

        Expect.True(identity is null, "a 404 should be reported as null (no such table).");
    }

    [Fact]
    public async Task TryGetEntityIdentityAsync_ServerError_Throws()
    {
        var (client, _) = Client(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.TryGetEntityIdentityAsync(EnvironmentUrl, "token", "bogdan_tablecreatetest", CancellationToken.None));
    }

    // ---- finding forms by name and type ----

    // The query filters on table, name AND type, so the three "Information" forms of a fresh table can be told apart.
    [Fact]
    public async Task FindSystemFormsAsync_FiltersOnTableNameAndType()
    {
        var (client, handler) = Client(HttpStatusCode.OK, """{"value":[]}""");

        await client.FindSystemFormsAsync(EnvironmentUrl, "token", "bogdan_tablecreatetest", "Information", 2, CancellationToken.None);

        var path = Decoded(handler);
        Expect.Contains("systemforms?", path, "forms are read from the systemforms collection.");
        Expect.Contains("objecttypecode eq 'bogdan_tablecreatetest'", path, "the query should filter on the table.");
        Expect.Contains("name eq 'Information'", path, "the query should filter on the name.");
        Expect.Contains("type eq 2", path, "the query should filter on the form type, which is what tells the three 'Information' forms apart.");
    }

    // Two matches come back as two results (the service, not the client, refuses).
    [Fact]
    public async Task FindSystemFormsAsync_TwoMatches_ReturnsBothWithTheirIds()
    {
        var first = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
        var second = new Guid("bbbbbbbb-0000-0000-0000-000000000002");
        var (client, _) = Client(HttpStatusCode.OK, $$"""{"value":[{"formid":"{{first}}","name":"Information","objecttypecode":"bogdan_tablecreatetest","formxml":"<form/>"},{"formid":"{{second}}","name":"Information","objecttypecode":"bogdan_tablecreatetest","formxml":"<form/>"}]}""");

        var forms = await client.FindSystemFormsAsync(EnvironmentUrl, "token", "bogdan_tablecreatetest", "Information", 2, CancellationToken.None);

        Expect.Sequence([first, second], forms.Select(f => f.FormId).ToList(), "every matching form should be returned, so the caller can list their ids.");
        Expect.Equal("<form/>", forms[0].FormXml, "each form's FormXML should be returned for the update path.");
    }

    [Fact]
    public async Task FindSystemFormsAsync_NoMatch_ReturnsAnEmptyList()
    {
        var (client, _) = Client(HttpStatusCode.OK, """{"value":[]}""");

        var forms = await client.FindSystemFormsAsync(EnvironmentUrl, "token", "bogdan_tablecreatetest", "Invoice Centre", 2, CancellationToken.None);

        Expect.Equal(0, forms.Count, "no matching form should be an empty list (which the service turns into a create), not an exception.");
    }
}

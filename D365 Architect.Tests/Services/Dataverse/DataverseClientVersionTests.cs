using System.Net;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="DataverseClient.GetSavedQueryVersionsAsync"/> and <see cref="DataverseClient.GetSystemFormVersionsAsync"/>:
/// the one cheap request per table and kind that tells <c>solution import</c> whether a view or form changed in Dataverse,
/// instead of loading each one in full. Tagged with the "Feature" trait "skip-unchanged-assets".
/// </summary>
[Trait("Feature", "skip-unchanged-assets")]
public sealed class DataverseClientVersionTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");
    private static readonly Guid FirstId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid SecondId = new("2b0c3c0e-6f1a-4c0e-9a51-6d3f0c7a1e22");

    /// <summary>Answers every request with one canned body and records the paths asked for - no real HTTP call ever happens.</summary>
    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(Uri.UnescapeDataString(request.RequestUri!.PathAndQuery));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static (DataverseClient Client, CannedHandler Handler) CreateClient(string body)
    {
        var handler = new CannedHandler(body);
        return (new DataverseClient(new HttpClient(handler)), handler);
    }

    // REQ-7: the live versions come from one query per table, not one load per view.
    // Code path: DataverseClient.GetSavedQueryVersionsAsync -> savedqueries?$select=savedqueryid,versionnumber&$filter=returnedtypecode eq '<table>'.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task GetSavedQueryVersionsAsync_Table_AsksOnceForIdAndVersionOfThatTablesViews()
    {
        var (client, handler) = CreateClient($$"""{"value":[{"savedqueryid":"{{FirstId}}","versionnumber":100},{"savedqueryid":"{{SecondId}}","versionnumber":12345678901}]}""");

        var versions = await client.GetSavedQueryVersionsAsync(EnvironmentUrl, "token", "tn_project", CancellationToken.None);

        var path = Assert.Single(handler.RequestedPaths);
        Expect.Contains("savedqueries?", path, "REQ-7: the versions should be read from the savedqueries table.");
        Expect.Contains("$select=savedqueryid,versionnumber", path, "REQ-7: only the id and version number should be selected, never the FetchXML or layout.");
        Expect.Contains("returnedtypecode eq 'tn_project'", path, "REQ-7: the query should be limited to the one table's views.");
        Expect.Equal(100L, versions[FirstId], "REQ-7: the first view's version number should be returned against its savedqueryid.");
        Expect.Equal(12345678901L, versions[SecondId], "REQ-7: a version number above int range should survive.");
    }

    // Code path: DataverseClient.GetSystemFormVersionsAsync -> systemforms?$select=formid,versionnumber&$filter=objecttypecode eq '<table>'.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task GetSystemFormVersionsAsync_Table_AsksOnceForIdAndVersionOfThatTablesForms()
    {
        var (client, handler) = CreateClient($$"""{"value":[{"formid":"{{FirstId}}","versionnumber":200},{"formid":"{{SecondId}}","versionnumber":12345678901}]}""");

        var versions = await client.GetSystemFormVersionsAsync(EnvironmentUrl, "token", "tn_project", CancellationToken.None);

        var path = Assert.Single(handler.RequestedPaths);
        Expect.Contains("systemforms?", path, "REQ-7: the versions should be read from the systemforms table.");
        Expect.Contains("$select=formid,versionnumber", path, "REQ-7: only the id and version number should be selected, never the FormXML.");
        Expect.Contains("objecttypecode eq 'tn_project'", path, "REQ-7: the query should be limited to the one table's forms.");
        Expect.Equal(200L, versions[FirstId], "REQ-7: the first form's version number should be returned against its formid.");
        Expect.Equal(12345678901L, versions[SecondId], "REQ-7: a version number above int range should survive.");
    }

    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task GetSavedQueryVersionsAsync_TableWithNoViews_ReturnsAnEmptyDictionary()
    {
        var (client, _) = CreateClient("""{"value":[]}""");

        var versions = await client.GetSavedQueryVersionsAsync(EnvironmentUrl, "token", "tn_project", CancellationToken.None);

        Expect.Equal(0, versions.Count, "REQ-7: a table with no views should give no versions, not an error.");
    }

    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task GetSystemFormVersionsAsync_TableWithNoForms_ReturnsAnEmptyDictionary()
    {
        var (client, _) = CreateClient("""{"value":[]}""");

        var versions = await client.GetSystemFormVersionsAsync(EnvironmentUrl, "token", "tn_project", CancellationToken.None);

        Expect.Equal(0, versions.Count, "REQ-7: a table with no forms should give no versions, not an error.");
    }
}

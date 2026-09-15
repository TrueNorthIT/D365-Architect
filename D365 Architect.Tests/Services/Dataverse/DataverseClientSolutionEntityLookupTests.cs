using System.Net;
using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="DataverseClient.TryGetSolutionEntityLogicalNamesAsync"/> —
/// the one solution-scoping lookup that needs two chained requests instead
/// of one (<c>solutioncomponents</c>, same as every other
/// <c>TryGetSolutionXxxIdsAsync</c>, followed by a bulk <c>EntityDefinitions</c>
/// fetch to resolve each Entity component's own MetadataId to a logical
/// name — see that method's own doc comment for why there's no cheaper way
/// to do that second step).
/// </summary>
public sealed class DataverseClientSolutionEntityLookupTests
{
    /// <summary>Routes a canned JSON response per relative path substring — no real HTTP call ever happens.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];
        public Dictionary<string, string> ResponseByPathSubstring { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            RequestedPaths.Add(path);

            var match = ResponseByPathSubstring.First(kvp => path.Contains(kvp.Key, StringComparison.Ordinal));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(match.Value) });
        }
    }

    private static (DataverseClient Client, RoutingHandler Handler) CreateClient()
    {
        var handler = new RoutingHandler();
        return (new DataverseClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task TryGetSolutionEntityLogicalNamesAsync_SolutionNotFound_ReturnsNullWithoutFurtherRequests()
    {
        var (client, handler) = CreateClient();
        handler.ResponseByPathSubstring["solutions?"] = """{"value":[]}""";

        var result = await client.TryGetSolutionEntityLogicalNamesAsync(new Uri("https://test.crm.dynamics.com"), "token", "nonexistent", CancellationToken.None);

        Assert.Null(result);
        Assert.Single(handler.RequestedPaths);
    }

    [Fact]
    public async Task TryGetSolutionEntityLogicalNamesAsync_SolutionWithNoEntityComponents_ReturnsEmptyWithoutFetchingEntityDefinitions()
    {
        var (client, handler) = CreateClient();
        var solutionId = Guid.NewGuid();
        handler.ResponseByPathSubstring["solutions?"] = $$"""{"value":[{"solutionid":"{{solutionId}}"}]}""";
        handler.ResponseByPathSubstring["solutioncomponents?"] = """{"value":[]}""";

        var result = await client.TryGetSolutionEntityLogicalNamesAsync(new Uri("https://test.crm.dynamics.com"), "token", "choicesonly", CancellationToken.None);

        Assert.Empty(result!);
        Assert.Equal(2, handler.RequestedPaths.Count);
        Assert.DoesNotContain(handler.RequestedPaths, p => p.Contains("EntityDefinitions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IsSolutionEntityIncludingSubcomponentsAsync_TableNotAComponentOfTheSolution_ReturnsFalse()
    {
        var (client, handler) = CreateClient();
        var solutionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        handler.ResponseByPathSubstring["solutions?"] = $$"""{"value":[{"solutionid":"{{solutionId}}"}]}""";
        handler.ResponseByPathSubstring["EntityDefinitions(LogicalName="] = $$"""{"MetadataId":"{{entityId}}"}""";
        handler.ResponseByPathSubstring["solutioncomponents?"] = """{"value":[]}""";

        var result = await client.IsSolutionEntityIncludingSubcomponentsAsync(new Uri("https://test.crm.dynamics.com"), "token", "examplesolution", "tn_widget", CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>
    /// The confirmed-live real-world case this method exists for (see its
    /// own doc comment): a table created inside a solution gets a single
    /// Entity solutioncomponent with <c>rootcomponentbehavior</c> 0, and
    /// Dataverse never separately lists its Attribute/View/SystemForm
    /// components at all.
    /// </summary>
    [Fact]
    public async Task IsSolutionEntityIncludingSubcomponentsAsync_EntityComponentWithRootComponentBehaviorZero_ReturnsTrue()
    {
        var (client, handler) = CreateClient();
        var solutionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        handler.ResponseByPathSubstring["solutions?"] = $$"""{"value":[{"solutionid":"{{solutionId}}"}]}""";
        handler.ResponseByPathSubstring["EntityDefinitions(LogicalName="] = $$"""{"MetadataId":"{{entityId}}"}""";
        handler.ResponseByPathSubstring["solutioncomponents?"] = """{"value":[{"rootcomponentbehavior":0}]}""";

        var result = await client.IsSolutionEntityIncludingSubcomponentsAsync(new Uri("https://test.crm.dynamics.com"), "token", "examplesolution", "tn_widget", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsSolutionEntityIncludingSubcomponentsAsync_NullRootComponentBehavior_TreatedAsIncludeSubcomponentsToo()
    {
        var (client, handler) = CreateClient();
        var solutionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        handler.ResponseByPathSubstring["solutions?"] = $$"""{"value":[{"solutionid":"{{solutionId}}"}]}""";
        handler.ResponseByPathSubstring["EntityDefinitions(LogicalName="] = $$"""{"MetadataId":"{{entityId}}"}""";
        handler.ResponseByPathSubstring["solutioncomponents?"] = """{"value":[{"rootcomponentbehavior":null}]}""";

        var result = await client.IsSolutionEntityIncludingSubcomponentsAsync(new Uri("https://test.crm.dynamics.com"), "token", "examplesolution", "tn_widget", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsSolutionEntityIncludingSubcomponentsAsync_RootComponentBehaviorDoNotIncludeSubcomponents_ReturnsFalse()
    {
        var (client, handler) = CreateClient();
        var solutionId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        handler.ResponseByPathSubstring["solutions?"] = $$"""{"value":[{"solutionid":"{{solutionId}}"}]}""";
        handler.ResponseByPathSubstring["EntityDefinitions(LogicalName="] = $$"""{"MetadataId":"{{entityId}}"}""";
        handler.ResponseByPathSubstring["solutioncomponents?"] = """{"value":[{"rootcomponentbehavior":1}]}""";

        var result = await client.IsSolutionEntityIncludingSubcomponentsAsync(new Uri("https://test.crm.dynamics.com"), "token", "examplesolution", "tn_widget", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryGetSolutionEntityLogicalNamesAsync_ResolvesComponentMetadataIdsToLogicalNames_IgnoringEntitiesOutsideTheSet()
    {
        var (client, handler) = CreateClient();
        var solutionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var opportunityId = Guid.NewGuid(); // in the environment, but not a component of this solution

        handler.ResponseByPathSubstring["solutions?"] = $$"""{"value":[{"solutionid":"{{solutionId}}"}]}""";
        handler.ResponseByPathSubstring["solutioncomponents?"] = $$"""
            {"value":[{"objectid":"{{contactId}}"},{"objectid":"{{accountId}}"}]}
            """;
        handler.ResponseByPathSubstring["EntityDefinitions?"] = $$"""
            {"value":[
                {"LogicalName":"opportunity","MetadataId":"{{opportunityId}}"},
                {"LogicalName":"contact","MetadataId":"{{contactId}}"},
                {"LogicalName":"account","MetadataId":"{{accountId}}"}
            ]}
            """;

        var result = await client.TryGetSolutionEntityLogicalNamesAsync(new Uri("https://test.crm.dynamics.com"), "token", "examplesolution", CancellationToken.None);

        Assert.Equal(["account", "contact"], result);
    }
}

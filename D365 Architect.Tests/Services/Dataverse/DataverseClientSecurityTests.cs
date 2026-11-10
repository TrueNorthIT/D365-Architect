using System.Net;
using System.Text.Json.Nodes;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Issue #21 review follow-up. Covers the security-role requests <see cref="DataverseClient"/> sends and how it reads the answers, against
/// canned responses (no real HTTP call). Shapes per Microsoft Learn's Web API reference: RetrieveRolePrivilegesRole, AddPrivilegesRole,
/// RemovePrivilegeRole (parameter <c>Privilege</c>, entity type <c>privilege</c>), EntityMetadata.Privileges (SecurityPrivilegeMetadata).
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class DataverseClientSecurityTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");
    private static readonly Guid RootBusinessUnit = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid RoleId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid CreateActivity = Guid.Parse("c0000000-0000-0000-0000-0000000000ac");
    private static readonly Guid ExportToExcel = Guid.Parse("c0000000-0000-0000-0000-0000000000ee");

    /// <summary>Answers by the first path substring that matches (in insertion order) and records every request with its body.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];
        public List<(string PathSubstring, string Json)> Responses { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            Requests.Add((request.Method, path, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            var match = Responses.FirstOrDefault(r => path.Contains(r.PathSubstring, StringComparison.Ordinal));
            return match.Json is null
                ? throw new InvalidOperationException($"No canned response for '{path}'.")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(match.Json) };
        }
    }

    private static (DataverseClient Client, RoutingHandler Handler) CreateClient()
    {
        var handler = new RoutingHandler();
        handler.Responses.Add(("businessunits?", $$"""{"value":[{"businessunitid":"{{RootBusinessUnit}}"}]}"""));
        handler.Responses.Add(("EntityDefinitions?", $$"""
            {"value":[
              {"LogicalName":"email","Privileges":[{"PrivilegeId":"{{CreateActivity}}","Name":"prvCreateActivity","PrivilegeType":"Create"}]},
              {"LogicalName":"appointment","Privileges":[{"PrivilegeId":"{{CreateActivity}}","Name":"prvCreateActivity","PrivilegeType":"Create"}]},
              {"LogicalName":"tn_complaint","Privileges":[]}
            ]}
            """));
        handler.Responses.Add(("privileges?", $$"""{"value":[{"privilegeid":"{{CreateActivity}}","name":"prvCreateActivity"},{"privilegeid":"{{ExportToExcel}}","name":"prvExportToExcel"}]}"""));
        return (new DataverseClient(new HttpClient(handler)), handler);
    }

    // 1: RemovePrivilegeRole's one parameter is `Privilege`, entity type `privilege` (Microsoft Learn, RemovePrivilegeRole Action).
    [Fact]
    [Trait("Requirement", "REVIEW-1")]
    public async Task RemoveRolePrivilegesAsync_SendsThePrivilegeAsAnEntityReference()
    {
        var (client, handler) = CreateClient();

        await client.RemoveRolePrivilegesAsync(EnvironmentUrl, "token", RoleId, [PrivilegeIdentity.ForName("prvExportToExcel")], CancellationToken.None);

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Expect.Contains($"roles({RoleId})/Microsoft.Dynamics.CRM.RemovePrivilegeRole", post.Path, "RemovePrivilegeRole is bound to the role.");
        var privilege = JsonNode.Parse(post.Body!)!["Privilege"]!;
        Expect.Equal("Microsoft.Dynamics.CRM.privilege", (string)privilege["@odata.type"]!, "the Privilege parameter is a privilege entity.");
        Expect.Equal(ExportToExcel, privilege["privilegeid"]!.GetValue<Guid>(), "the entity is identified by privilegeid.");
    }

    // 3: two root business unit roles with one name: an error naming it, never the first one.
    [Fact]
    [Trait("Requirement", "REVIEW-3")]
    public async Task TryGetRoleByNameAsync_TwoRootBusinessUnitRolesWithTheName_Throws()
    {
        var (client, handler) = CreateClient();
        handler.Responses.Insert(0, ("roles?", $$"""{"value":[{"roleid":"{{Guid.NewGuid()}}","name":"HIS User"},{"roleid":"{{Guid.NewGuid()}}","name":"his user"}]}"""));

        var ex = await Assert.ThrowsAsync<DuplicateRoleNameException>(() => client.TryGetRoleByNameAsync(EnvironmentUrl, "token", "HIS User", CancellationToken.None));

        Expect.Contains("HIS User", ex.Message, "the error should name the role.");
    }

    // 6: RetrieveRolePrivilegesRole without PrivilegeName: the name is looked up by id, so a non-table privilege isn't dropped.
    [Fact]
    [Trait("Requirement", "REVIEW-6")]
    public async Task GetRolePrivilegesAsync_NoPrivilegeNameReturned_LooksTheNameUpById()
    {
        var (client, handler) = CreateClient();
        handler.Responses.Insert(0, ("RetrieveRolePrivilegesRole", $$"""{"RolePrivileges":[{"PrivilegeId":"{{ExportToExcel}}","Depth":"Global","BusinessUnitId":"{{RootBusinessUnit}}"}]}"""));

        var privileges = await client.GetRolePrivilegesAsync(EnvironmentUrl, "token", RoleId, CancellationToken.None);

        var held = Assert.Single(privileges);
        Expect.Equal("prvExportToExcel", held.Privilege.PrivilegeName ?? "<null>", "the name should come from the privileges table, by id.");
        Expect.Equal(PrivilegeDepth.Global, held.Depth, "the depth should be read.");
    }

    // A: a shared privilege resolves to one id with every table it stands for.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task ResolvePrivilegesAsync_ASharedPrivilege_ResolvesToOneIdWithEveryTable()
    {
        var (client, _) = CreateClient();

        var resolved = await client.ResolvePrivilegesAsync(EnvironmentUrl, "token",
            [PrivilegeIdentity.ForTable("email", TablePrivilege.Create), PrivilegeIdentity.ForTable("tn_complaint", TablePrivilege.Assign), PrivilegeIdentity.ForName("prvExportToExcel")], CancellationToken.None);

        Expect.Equal(CreateActivity, resolved[0]!.PrivilegeId, "Create on email is the shared prvCreateActivity.");
        Expect.Sequence(["appointment", "email"], resolved[0]!.Tables.Select(t => t.Table).OrderBy(t => t, StringComparer.Ordinal).ToList(), "it stands for Create on every table that shares it.");
        Expect.True(resolved[1] is null, "a right the table doesn't have resolves to null.");
        Expect.Equal(ExportToExcel, resolved[2]!.PrivilegeId, "a non-table privilege resolves by name.");
        Expect.Empty(resolved[2]!.Tables, "a non-table privilege has no tables.");
    }

    // A: two entries for one privilege at different depths are refused, never one silently picked.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task AddRolePrivilegesAsync_TwoEntriesForOnePrivilegeAtDifferentDepths_Throws()
    {
        var (client, handler) = CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.AddRolePrivilegesAsync(EnvironmentUrl, "token", RoleId,
            [new RolePrivilege(PrivilegeIdentity.ForTable("email", TablePrivilege.Create), PrivilegeDepth.Basic), new RolePrivilege(PrivilegeIdentity.ForTable("appointment", TablePrivilege.Create), PrivilegeDepth.Global)], CancellationToken.None));

        Expect.True(handler.Requests.All(r => r.Method != HttpMethod.Post), "nothing should be sent.");
    }
}

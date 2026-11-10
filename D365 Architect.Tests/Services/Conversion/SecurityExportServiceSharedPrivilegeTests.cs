using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 review follow-up: a privilege that one id stands for on a table of the solution and on tables outside it (the activity tables
/// share <c>prvCreateActivity</c>) belongs to the solution table's security file, so the role file doesn't list it again.
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityExportServiceSharedPrivilegeTests
{
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task ExportSecurityAsync_ASharedPrivilegeAlsoOnASolutionTable_IsLeftOutOfTheRoleFile()
    {
        var client = ExportEnvironment();
        client.SharedPrivileges.Add((Guid.Parse("c0000000-0000-0000-0000-0000000000ac"), "prvCreateActivity",
            [new TablePrivilegeRef(Complaint, TablePrivilege.Create), new TablePrivilegeRef("email", TablePrivilege.Create)]));
        client.RolePrivileges[HisUserId] = [.. client.RolePrivileges[HisUserId], OnTable("email", TablePrivilege.Create, PrivilegeDepth.Basic)];

        var result = await new SecurityExportService(client).ExportSecurityAsync(EnvironmentUrl, "token", SolutionName, null, ["HIS User"], CancellationToken.None);

        var role = RoleYamlDeserializer.FromYaml(Assert.Single(result.Roles).Yaml);
        Expect.True(role.Tables is null || !role.Tables.ContainsKey("email"),
            "Create on email is the same privilege as Create on tn_complaint, which tn_complaint's security file already states: the role file shouldn't state it again.");
        Expect.True(role.Tables!.ContainsKey("account"), "an unshared outside-table privilege is still listed.");
    }
}

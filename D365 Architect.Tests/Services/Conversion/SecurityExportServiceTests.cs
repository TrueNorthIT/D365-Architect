using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 (security role privileges as YAML). Covers <see cref="SecurityExportService"/>: from the roles of a solution and what each holds
/// (<see cref="FakeDataverseClient"/>, built by <see cref="SecurityFixtures.ExportEnvironment"/>) it produces a <c>*.security.yml</c> per
/// table and a <c>*.role.yml</c> per role. The story is the requirements' own: solution "qad" holds the roles "HIS User" and "HIS Super User"
/// and the tables tn_complaint and tn_lookupcode; "System Administrator" has full access but is not in the solution.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityExportServiceTests
{
    private static Task<SecurityExportResult> Export(FakeDataverseClient client, IReadOnlyCollection<string>? tables = null, IReadOnlyCollection<string>? roles = null) =>
        new SecurityExportService(client).ExportSecurityAsync(EnvironmentUrl, "token", SolutionName, tables, roles, CancellationToken.None);

    private static TableSecurityDefinition TableFile(SecurityExportResult result, string table)
    {
        var file = result.Tables.SingleOrDefault(t => string.Equals(t.TableLogicalName, table, StringComparison.OrdinalIgnoreCase));
        Assert.True(file is not null, $"expected a security file for table '{table}'. Tables with a file: <{string.Join(", ", result.Tables.Select(t => t.TableLogicalName))}>.");
        return TableSecurityYamlDeserializer.FromYaml(file!.Yaml);
    }

    private static RoleDefinition RoleFile(SecurityExportResult result, string roleName)
    {
        var file = result.Roles.SingleOrDefault(r => string.Equals(r.RoleName, roleName, StringComparison.OrdinalIgnoreCase));
        Assert.True(file is not null, $"expected a role file for '{roleName}'. Role files: <{string.Join(", ", result.Roles.Select(r => r.RoleName))}>.");
        return RoleYamlDeserializer.FromYaml(file!.Yaml);
    }

    // REQ-1: "`solution export` writes a security file into each table's folder listing every role in the solution that has any privilege on that table."
    // Example: solution qad contains roles "HIS User" and "HIS Super User"; tn_complaint/ gets tn_complaint.security.yml listing both.
    // Code path: SecurityExportService.ExportSecurityAsync -> SecurityExportResult.Tables.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task ExportSecurityAsync_ListsEveryRoleOfTheSolutionThatHoldsAnyPrivilegeOnTheTable()
    {
        var result = await Export(ExportEnvironment());

        var file = TableFile(result, Complaint);
        Expect.Equal(Complaint, file.Table, "REQ-1: the security file should name its table.");
        Expect.Equal(2, file.Roles.Count, "REQ-1: tn_complaint's security file should list both roles of the solution.");
        Expect.Contains("HIS User", string.Join("|", file.Roles.Select(r => r.Name)), "REQ-1: 'HIS User' should be listed.");
        Expect.Contains("HIS Super User", string.Join("|", file.Roles.Select(r => r.Name)), "REQ-1: 'HIS Super User' should be listed.");
    }

    // REQ-1: a role is listed when it has ANY privilege on the table, not only when it has all of them.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task ExportSecurityAsync_ARoleWithOnlyOnePrivilegeOnTheTableIsStillListed()
    {
        var client = ExportEnvironment();
        client.RolePrivileges[HisUserId] = [OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic)];

        var result = await Export(client);

        var hisUser = TableFile(result, Complaint).Roles.SingleOrDefault(r => r.Name == "HIS User");
        Expect.True(hisUser is not null, "REQ-1: a role holding just Share on the table should still be listed for it.");
        Expect.Equal(SecurityAccessLevel.User, hisUser!.Share, "the one privilege held should be shown at its level.");
    }

    // REQ-2: "Each role in the file shows all 8 privileges with their access level."
    // Example: HIS User on tn_complaint: Create User, Read Organisation, Write BusinessUnit, Delete None, Append User, AppendTo Organisation, Assign None, Share None.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public async Task ExportSecurityAsync_ShowsAllEightPrivilegesForARoleWithTheirAccessLevel()
    {
        var result = await Export(ExportEnvironment());

        var hisUser = TableFile(result, Complaint).Roles.Single(r => r.Name == "HIS User");
        Expect.Equal(SecurityAccessLevel.User, hisUser.Create, "REQ-2: HIS User's Create on tn_complaint should be User.");
        Expect.Equal(SecurityAccessLevel.Organisation, hisUser.Read, "REQ-2: HIS User's Read on tn_complaint should be Organisation.");
        Expect.Equal(SecurityAccessLevel.BusinessUnit, hisUser.Write, "REQ-2: HIS User's Write on tn_complaint should be BusinessUnit.");
        Expect.Equal(SecurityAccessLevel.None, hisUser.Delete, "REQ-2: HIS User does not hold Delete on tn_complaint, so it should be None.");
        Expect.Equal(SecurityAccessLevel.User, hisUser.Append, "REQ-2: HIS User's Append on tn_complaint should be User.");
        Expect.Equal(SecurityAccessLevel.Organisation, hisUser.AppendTo, "REQ-2: HIS User's AppendTo on tn_complaint should be Organisation.");
        Expect.Equal(SecurityAccessLevel.None, hisUser.Assign, "REQ-2: HIS User does not hold Assign on tn_complaint, so it should be None.");
        Expect.Equal(SecurityAccessLevel.None, hisUser.Share, "REQ-2: HIS User does not hold Share on tn_complaint, so it should be None.");
    }

    // REQ-2 (with the mapping): all four depths come out as their access level, in the file text itself.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public async Task ExportSecurityAsync_WritesEachDepthAsItsAccessLevelName()
    {
        var client = ExportEnvironment();
        client.RolePrivileges[HisUserId] =
        [
            OnTable(Complaint, TablePrivilege.Create, PrivilegeDepth.Basic),
            OnTable(Complaint, TablePrivilege.Read, PrivilegeDepth.Local),
            OnTable(Complaint, TablePrivilege.Write, PrivilegeDepth.Deep),
            OnTable(Complaint, TablePrivilege.Delete, PrivilegeDepth.Global),
        ];

        var result = await Export(client);

        var yaml = result.Tables.Single(t => t.TableLogicalName == Complaint).Yaml;
        foreach (var expected in new[] { "create: User", "read: BusinessUnit", "write: ParentChildBusinessUnit", "delete: Organisation" })
        {
            Expect.Contains(expected, yaml, "REQ-2: privilege depth Basic/Local/Deep/Global should be written as User/BusinessUnit/ParentChildBusinessUnit/Organisation.");
        }
    }

    // REQ-3: "Roles not in the solution are left out even if they have access."
    // Example: System Administrator has full access but isn't in qad -> not in the file. The fake throws if its privileges are read.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public async Task ExportSecurityAsync_LeavesOutARoleThatIsNotInTheSolutionEvenWithFullAccess()
    {
        var client = ExportEnvironment();
        client.RolePrivileges[SystemAdministratorId] = AllEightOn(Complaint, PrivilegeDepth.Global);

        var result = await Export(client);

        var allYaml = string.Join("\n", result.Tables.Select(t => t.Yaml).Concat(result.Roles.Select(r => r.Yaml)));
        Expect.True(!allYaml.Contains("System Administrator", StringComparison.Ordinal), $"REQ-3: a role that is not in the solution should not appear in any file. Actual: <{allYaml}>.");
        Expect.True(!client.RolePrivilegeReads.Contains(SystemAdministratorId), "REQ-3: only the solution's own roles should be read.");
        Expect.Equal(2, result.Roles.Count, "REQ-13/REQ-3: there should be a role file for each of the solution's two roles, and none for System Administrator.");
    }

    // REQ-4: "A table no solution role has access to gets no security file (never an empty one)."
    // Example: No qad role grants access to tn_lookupcode -> no .security.yml there.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task ExportSecurityAsync_ATableNoSolutionRoleCanAccessGetsNoSecurityFile()
    {
        var result = await Export(ExportEnvironment());

        Expect.True(result.Tables.All(t => t.TableLogicalName != LookupCode),
            $"REQ-4: tn_lookupcode, which no role of the solution can access, should have no security file. Tables with a file: <{string.Join(", ", result.Tables.Select(t => t.TableLogicalName))}>.");
        Expect.True(result.Tables.All(t => !string.IsNullOrWhiteSpace(t.Yaml)), "REQ-4: a security file is never empty.");
    }

    // REQ-4: a role that is in the solution but has nothing on a table does not make that table get a file listing nobody.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task ExportSecurityAsync_WhenNoRoleHoldsAnythingOnAnyTableNoTableFileIsWritten()
    {
        var client = ExportEnvironment();
        client.RolePrivileges[HisUserId] = [Named("prvExportToExcel", PrivilegeDepth.Global)];
        client.RolePrivileges[HisSuperUserId] = [];

        var result = await Export(client);

        Expect.Empty(result.Tables, "REQ-4: with no role holding any table privilege, no security file should be written at all.");
    }

    // REQ-5: "Roles are identified by name, so the file works across dev/test/prod." The files say "HIS User", not an id.
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public async Task ExportSecurityAsync_IdentifiesRolesByNameNotByGuid()
    {
        var result = await Export(ExportEnvironment());

        var allYaml = string.Join("\n", result.Tables.Select(t => t.Yaml).Concat(result.Roles.Select(r => r.Yaml)));
        Expect.Contains("HIS User", allYaml, "REQ-5: the role should be named in the files.");
        foreach (var id in new[] { HisUserId, HisSuperUserId })
        {
            Expect.True(!allYaml.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase), $"REQ-5: a role's id ({id}) should never be written, as it differs in every environment.");
        }
    }

    // REQ-13: "`solution export` writes one file per solution role under `roles/` at the solution root."
    // Example: roles/his-user.role.yml, roles/his-super-user.role.yml.
    [Fact]
    [Trait("Requirement", "REQ-13")]
    public async Task ExportSecurityAsync_WritesOneRoleFilePerSolutionRoleNamedAfterTheRole()
    {
        var result = await Export(ExportEnvironment());

        Expect.Equal(2, result.Roles.Count, "REQ-13: one role file per role in the solution.");
        Expect.Equal("his-user", result.Roles.Single(r => r.RoleName == "HIS User").FileNameStem, "REQ-13: 'HIS User' should be written as his-user.role.yml.");
        Expect.Equal("his-super-user", result.Roles.Single(r => r.RoleName == "HIS Super User").FileNameStem, "REQ-13: 'HIS Super User' should be written as his-super-user.role.yml.");
    }

    // REQ-13: every role of the solution gets a file, including one that holds nothing outside the table files.
    [Fact]
    [Trait("Requirement", "REQ-13")]
    public async Task ExportSecurityAsync_ARoleWithNoNonTablePrivilegesStillGetsARoleFile()
    {
        var result = await Export(ExportEnvironment());

        var superUser = RoleFile(result, "HIS Super User");
        Expect.Equal("HIS Super User", superUser.Name, "REQ-13: HIS Super User holds only tn_complaint privileges (in the table file), but should still have a role file naming it.");
        Expect.True((superUser.Privileges?.Count ?? 0) == 0 && (superUser.Tables?.Count ?? 0) == 0, "REQ-17: HIS Super User's tn_complaint privileges live in the table file, so its role file should list nothing else.");
    }

    // REQ-13: two roles whose names slug the same do not overwrite each other's file.
    [Fact]
    [Trait("Requirement", "REQ-13")]
    public async Task ExportSecurityAsync_RolesWhoseNamesSlugTheSameGetDifferentFileNames()
    {
        var client = ExportEnvironment();
        var other = new SecurityRole(Guid.Parse("a0000000-0000-0000-0000-0000000000f1"), "HIS  User!");
        client.SolutionRoles = _ => [HisUser(), other];
        client.RolePrivileges[other.RoleId] = [];

        var result = await Export(client);

        Expect.Equal(2, result.Roles.Select(r => r.FileNameStem).Distinct(StringComparer.OrdinalIgnoreCase).Count(), $"REQ-13: two roles should never share a file name. Stems: <{string.Join(", ", result.Roles.Select(r => r.FileNameStem))}>.");
    }

    // REQ-14: "Role file lists non-table privileges by platform privilege name with access level." Example: prvExportToExcel: Organisation.
    [Fact]
    [Trait("Requirement", "REQ-14")]
    public async Task ExportSecurityAsync_RoleFileListsNonTablePrivilegesByPlatformNameWithAccessLevel()
    {
        var result = await Export(ExportEnvironment());

        var hisUser = RoleFile(result, "HIS User");
        Expect.Equal(SecurityAccessLevel.Organisation, hisUser.Privileges!["prvExportToExcel"], "REQ-14: prvExportToExcel should be listed at Organisation.");
        Expect.Equal(SecurityAccessLevel.Organisation, hisUser.Privileges["prvGoMobile"], "REQ-14: prvGoMobile should be listed at Organisation.");
    }

    // REQ-15: "Role file lists only privileges the role holds; absent = not held." Example: HIS User lacks Bulk Delete -> no prvBulkDelete line.
    [Fact]
    [Trait("Requirement", "REQ-15")]
    public async Task ExportSecurityAsync_RoleFileHasNoLineForAPrivilegeTheRoleDoesNotHold()
    {
        var result = await Export(ExportEnvironment());

        var file = result.Roles.Single(r => r.RoleName == "HIS User");
        Expect.True(!file.Yaml.Contains("prvBulkDelete", StringComparison.Ordinal), $"REQ-15: HIS User does not hold Bulk Delete, so its role file should have no prvBulkDelete line. Actual: <{file.Yaml}>.");
        Expect.Equal(2, RoleYamlDeserializer.FromYaml(file.Yaml).Privileges!.Count, "REQ-15: only the two non-table privileges the role holds should be listed.");
    }

    // REQ-16: "Privileges on tables outside the solution go in the role file under `tables:` with the same 8 privilege keys." Example: tables: account: { read: Organisation }.
    [Fact]
    [Trait("Requirement", "REQ-16")]
    public async Task ExportSecurityAsync_PrivilegesOnTablesOutsideTheSolutionGoInTheRoleFileUnderTables()
    {
        var result = await Export(ExportEnvironment());

        var hisUser = RoleFile(result, "HIS User");
        Expect.Equal<SecurityAccessLevel?>(SecurityAccessLevel.Organisation, hisUser.Tables!["account"].Read, "REQ-16: Read on account (not in the solution) should be listed under tables: in the role file.");
        Expect.Equal<SecurityAccessLevel?>(null, hisUser.Tables["account"].Create, "REQ-15/16: a privilege on account the role does not hold should not be listed.");
    }

    // REQ-17: "Each table privilege lives in exactly one place: in-solution tables in `<table>.security.yml`, all others in the role file."
    // Example: tn_complaint never appears under a role file's `tables:`.
    [Fact]
    [Trait("Requirement", "REQ-17")]
    public async Task ExportSecurityAsync_ATableInTheSolutionNeverAppearsInARoleFilesTables()
    {
        var result = await Export(ExportEnvironment());

        foreach (var role in result.Roles.Select(r => RoleYamlDeserializer.FromYaml(r.Yaml)))
        {
            Expect.True(role.Tables is null || !role.Tables.ContainsKey(Complaint), $"REQ-17: tn_complaint is in the solution, so its privileges belong in its security file, not under '{role.Name}''s tables:.");
        }
    }

    // REQ-17: and the other way round: a table outside the solution never gets a security file.
    [Fact]
    [Trait("Requirement", "REQ-17")]
    public async Task ExportSecurityAsync_ATableOutsideTheSolutionNeverGetsASecurityFile()
    {
        var result = await Export(ExportEnvironment());

        Expect.True(result.Tables.All(t => t.TableLogicalName != "account"),
            $"REQ-17: account is not in the solution, so its privileges belong in the role files, not in an account security file. Tables with a file: <{string.Join(", ", result.Tables.Select(t => t.TableLogicalName))}>.");
    }

    // REQ-6: "optional repeatable `--table` limits to those tables' security files." Example: --table tn_complaint writes only tn_complaint's file.
    // The limit means table files only: "`--table` alone skips role files" (REQ-19).
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task ExportSecurityAsync_LimitedToATableGivesOnlyThatTablesFileAndNoRoleFiles()
    {
        var client = ExportEnvironment();
        client.RolePrivileges[HisUserId] = [.. HisUserOnComplaint(), OnTable(LookupCode, TablePrivilege.Read, PrivilegeDepth.Global)];

        var result = await Export(client, tables: ["TN_COMPLAINT"]);

        Expect.Sequence([Complaint], result.Tables.Select(t => t.TableLogicalName).ToList(), "REQ-6: --table (matched case-insensitively) should give only that table's security file.");
        Expect.Empty(result.Roles, "REQ-19: --table alone should skip the role files.");
    }

    // REQ-19: "Optional repeatable `--role` limits security export/import to those role files." Example: --role "HIS User" writes only roles/his-user.role.yml.
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public async Task ExportSecurityAsync_LimitedToARoleGivesOnlyThatRolesFileAndNoTableFiles()
    {
        var result = await Export(ExportEnvironment(), roles: ["his user"]);

        Expect.Sequence(["his-user"], result.Roles.Select(r => r.FileNameStem).ToList(), "REQ-19: --role (matched case-insensitively by name) should give only that role's file.");
        Expect.Empty(result.Tables, "REQ-19: --role alone should write no table security files.");
    }

    // REQ-6/19: both limits together give both kinds of file, each limited.
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public async Task ExportSecurityAsync_LimitedToATableAndARoleGivesBoth()
    {
        var result = await Export(ExportEnvironment(), tables: [Complaint], roles: ["HIS Super User"]);

        Expect.Sequence([Complaint], result.Tables.Select(t => t.TableLogicalName).ToList(), "REQ-6: the table limit should apply to the table files.");
        Expect.Sequence(["his-super-user"], result.Roles.Select(r => r.FileNameStem).ToList(), "REQ-19: the role limit should apply to the role files.");
    }

    // REQ-6: with no limit, `security export --solution x` writes every table file and every role file.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task ExportSecurityAsync_WithNoLimitGivesEveryTableFileAndEveryRoleFile()
    {
        var result = await Export(ExportEnvironment(), tables: [], roles: []);

        Expect.Equal(1, result.Tables.Count, "REQ-6: every table that some role can access should have a file (only tn_complaint here).");
        Expect.Equal(2, result.Roles.Count, "REQ-6: every role of the solution should have a file.");
    }

    // REQ-20: "A `--table` not in the solution / with no folder fails with a clear error, as `solution export --table` does."
    // Example: "Table 'tn_typo' is not in solution 'qad'". Code path: UnknownTableException (as SolutionTableFilter throws for solution export).
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ExportSecurityAsync_ATableNotInTheSolutionFailsNamingTheTableAndTheSolution()
    {
        var ex = await Assert.ThrowsAsync<UnknownTableException>(() => Export(ExportEnvironment(), tables: ["tn_typo"]));

        Expect.Contains("tn_typo", ex.Message, "REQ-20: the error should name the table that was not found.");
        Expect.Contains("qad", ex.Message, "REQ-20: the error should name the solution it was looked for in.");
    }

    // REQ-24: "`security export` / `security import` with a `--role "X"` where no role named X is in the solution fails with a clear error containing
    // the role name and solution name (e.g. "Role 'X' is not in solution 'qad'")." Code path: UnknownRoleException (as UnknownTableException for --table).
    // HIS Auditor exists in the environment but is not a role of the solution.
    [Fact]
    [Trait("Requirement", "REQ-24")]
    public async Task ExportSecurityAsync_ARoleNotInTheSolutionFailsNamingTheRoleAndTheSolution()
    {
        var client = ExportEnvironment();
        client.LiveRoles.Add(new SecurityRole(Guid.NewGuid(), "HIS Auditor"));

        var ex = await Assert.ThrowsAsync<UnknownRoleException>(() => Export(client, roles: ["HIS Auditor"]));

        Expect.Contains("HIS Auditor", ex.Message, "REQ-24: the error should name the role that is not in the solution.");
        Expect.Contains("qad", ex.Message, "REQ-24: the error should name the solution.");
    }

    // REQ-24: one good role does not excuse a bad one: the request fails as a whole.
    [Fact]
    [Trait("Requirement", "REQ-24")]
    public async Task ExportSecurityAsync_OneUnknownRoleAmongKnownOnesStillFails()
    {
        var ex = await Assert.ThrowsAsync<UnknownRoleException>(() => Export(ExportEnvironment(), roles: ["HIS User", "HIS Typo"]));

        Expect.Contains("HIS Typo", ex.Message, "REQ-24: the error should name the role that is not in the solution.");
        Expect.True(!ex.Message.Contains("'HIS User'", StringComparison.Ordinal), "REQ-24: the role that is in the solution should not be named as unknown.");
    }

    // As every other export: a solution that does not exist is SolutionNotFoundException.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task ExportSecurityAsync_SolutionDoesNotExist_ThrowsSolutionNotFound()
    {
        var client = new FakeDataverseClient { SolutionEntityLogicalNames = _ => null, SolutionRoles = _ => null };

        await Assert.ThrowsAsync<SolutionNotFoundException>(() => Export(client));
    }
}

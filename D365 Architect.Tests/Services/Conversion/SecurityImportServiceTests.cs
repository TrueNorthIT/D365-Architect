using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 (security role privileges as YAML). Covers <see cref="SecurityImportService"/>: previewing a table security file or a role file
/// against what the roles hold live (<see cref="FakeDataverseClient"/>, built by <see cref="SecurityFixtures.ImportEnvironment"/>) and
/// applying exactly what was previewed. In the fake environment "HIS User" holds, on tn_complaint, Create User, Read Organisation,
/// Write BusinessUnit, Append User and AppendTo Organisation (Delete, Assign and Share are not held); plus prvExportToExcel at
/// Organisation and Read on account at Organisation, which the table-file tests must never touch.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityImportServiceTests
{
    private static TableRoleSecurity HisUserRow(SecurityAccessLevel delete = SecurityAccessLevel.None, SecurityAccessLevel write = SecurityAccessLevel.BusinessUnit, SecurityAccessLevel share = SecurityAccessLevel.None, string name = "HIS User") => new()
    {
        Name = name,
        Create = SecurityAccessLevel.User,
        Read = SecurityAccessLevel.Organisation,
        Write = write,
        Delete = delete,
        Append = SecurityAccessLevel.User,
        AppendTo = SecurityAccessLevel.Organisation,
        Assign = SecurityAccessLevel.None,
        Share = share,
    };

    private static TableSecurityDefinition ComplaintFile(params TableRoleSecurity[] roles) => new() { Table = Complaint, Roles = roles };

    private static Task<SecurityImportPreview> PreviewTable(FakeDataverseClient client, TableSecurityDefinition file, bool allowRemovals = false) =>
        new SecurityImportService(client).PreviewTableSecurityAsync(EnvironmentUrl, "token", file, allowRemovals, CancellationToken.None);

    private static Task<SecurityImportPreview> PreviewRole(FakeDataverseClient client, RoleDefinition file, bool allowRemovals = false) =>
        new SecurityImportService(client).PreviewRoleAsync(EnvironmentUrl, "token", file, SolutionName, allowRemovals, CancellationToken.None);

    private static Task Apply(FakeDataverseClient client, SecurityImportPreview preview) =>
        new SecurityImportService(client).ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

    private static PrivilegeChange Single(SecurityImportPreview preview) => Assert.Single(Assert.Single(preview.Roles).Changes);

    // ---- Table security files (REQ-7 to REQ-10) ----

    // REQ-7: "shows a diff against live role privileges". Example: Change HIS User Delete None -> User: diff `Delete: None -> User`.
    // Code path: SecurityImportService.PreviewTableSecurityAsync -> SecurityImportPreview.Roles[].Changes.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task PreviewTableSecurity_RaisingDeleteFromNoneToUser_PlansExactlyThatOneChange()
    {
        var preview = await PreviewTable(ImportEnvironment(), ComplaintFile(HisUserRow(delete: SecurityAccessLevel.User)));

        var change = Single(preview);
        Expect.Equal(Complaint, change.Privilege.TableLogicalName ?? "", "REQ-7: the change should be on tn_complaint.");
        Expect.Equal<TablePrivilege?>(TablePrivilege.Delete, change.Privilege.TableRight, "REQ-7: the change should be to the Delete privilege.");
        Expect.Equal(SecurityAccessLevel.None, change.From, "REQ-7: Delete is None live.");
        Expect.Equal(SecurityAccessLevel.User, change.To, "REQ-7: the file asks for User.");
        Expect.Equal(PrivilegeChangeAction.Set, change.Action, "REQ-8: raising a level is a set (add-privilege) write.");
        Expect.True(preview.HasChanges, "REQ-7: the file differs from live, so the preview should have changes.");
    }

    // REQ-7: a file that already matches live is unchanged: no changes, nothing to ask about.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task PreviewTableSecurity_FileMatchesLive_HasNoChanges()
    {
        var preview = await PreviewTable(ImportEnvironment(), ComplaintFile(HisUserRow()));

        Expect.True(!preview.HasChanges, "REQ-7: a file that matches what the role holds live has nothing to import.");
        Expect.Empty(Assert.Single(preview.Roles).Changes, "REQ-7: no privilege differs, so none should be listed.");
    }

    // REQ-8: "Raising or lowering an access level updates the role in Dataverse (use add-privilege semantics, never replace-all,
    // so the role's other privileges are untouched)." Example: After confirming, HIS User can delete their own complaints.
    // Code path: SecurityImportService.ApplyAsync -> IDataverseClient.AddRolePrivilegesAsync (one call, only the privilege that changed).
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task ApplyTableSecurity_RaisingDelete_AddsOnlyThatPrivilegeAtBasicDepth()
    {
        var client = ImportEnvironment();
        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(delete: SecurityAccessLevel.User)));

        await Apply(client, preview);

        var (roleId, privileges) = Assert.Single(client.AddRolePrivilegesCalls);
        Expect.Equal(HisUserId, roleId, "REQ-8: the privilege should be added to HIS User.");
        var added = Assert.Single(privileges);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Delete, added.Privilege.TableRight, "REQ-8: only Delete changed, so only Delete should be added (the role's other privileges are untouched).");
        Expect.Equal(Complaint, added.Privilege.TableLogicalName ?? "", "REQ-8: Delete should be added on tn_complaint.");
        Expect.Equal(PrivilegeDepth.Basic, added.Depth, "REQ-8: access level User is privilege depth Basic.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-8: raising a level removes nothing.");
    }

    // REQ-8: "Raising or lowering": lowering Write from BusinessUnit to User is also one add, at the new depth.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task ApplyTableSecurity_LoweringWrite_AddsItAtTheLowerDepthAndRemovesNothing()
    {
        var client = ImportEnvironment();
        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(write: SecurityAccessLevel.User)));

        await Apply(client, preview);

        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Write, added.Privilege.TableRight, "REQ-8: the Write privilege changed.");
        Expect.Equal(PrivilegeDepth.Basic, added.Depth, "REQ-8: lowering Write from BusinessUnit to User should add it at depth Basic.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-8: lowering a level is not a removal.");
    }

    // REQ-8: a file that changes nothing writes nothing, however many times it is applied.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task ApplyTableSecurity_NothingChanged_WritesNothing()
    {
        var client = ImportEnvironment();
        var preview = await PreviewTable(client, ComplaintFile(HisUserRow()));

        await Apply(client, preview);

        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-8: nothing differs, so nothing should be added.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-8: nothing differs, so nothing should be removed.");
    }

    // REQ-8: "the role's other privileges are untouched": the table file only compares its own table's eight privileges.
    // HIS User also holds prvExportToExcel and Read on account live; neither is in the file and neither is mentioned.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task PreviewTableSecurity_NeverComparesPrivilegesOnOtherTablesOrNonTablePrivileges()
    {
        var preview = await PreviewTable(ImportEnvironment(), ComplaintFile(HisUserRow(delete: SecurityAccessLevel.User)));

        var change = Single(preview);
        Expect.Equal(Complaint, change.Privilege.TableLogicalName ?? "", "REQ-8: the table file should only ever plan changes on its own table; prvExportToExcel and Read on account must be left alone.");
    }

    // REQ-7: a role the file does not name is not touched, however much it holds.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task PreviewTableSecurity_OnlyPlansForTheRolesTheFileNames()
    {
        var client = ImportEnvironment();
        client.LiveRoles.Add(HisSuperUser());
        client.RolePrivileges[HisSuperUserId] = AllEightOn(Complaint, PrivilegeDepth.Global);

        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(delete: SecurityAccessLevel.User)));

        var plan = Assert.Single(preview.Roles);
        Expect.Equal("HIS User", plan.RoleName, "REQ-7: only the role the file names should be planned.");
        Expect.True(!client.RolePrivilegeReads.Contains(HisSuperUserId), "a role the file doesn't name has no business being read.");
    }

    // REQ-9: "A privilege held live but missing / None in the file is only removed with `--allow-removals`; otherwise kept and reported."
    // Example: Live Share=User, file None -> without flag "~ would remove Share (kept)".
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task PreviewTableSecurity_ALivePrivilegeTheFileSaysNone_WithoutAllowRemovals_IsKeptAndReported()
    {
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic));

        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(share: SecurityAccessLevel.None)), allowRemovals: false);

        var change = Single(preview);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Share, change.Privilege.TableRight, "REQ-9: the privilege held live but None in the file is Share.");
        Expect.Equal(SecurityAccessLevel.User, change.From, "REQ-9: Share is User live.");
        Expect.Equal(SecurityAccessLevel.None, change.To, "REQ-9: the file says None.");
        Expect.Equal(PrivilegeChangeAction.KeptNotRemoved, change.Action, "REQ-9: without --allow-removals the privilege is kept and reported, not removed.");
        Expect.True(!preview.HasChanges, "REQ-9: a kept privilege is not a write, so the file has nothing to import.");
    }

    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task ApplyTableSecurity_ALivePrivilegeTheFileSaysNone_WithoutAllowRemovals_RemovesNothing()
    {
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic));
        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(delete: SecurityAccessLevel.User, share: SecurityAccessLevel.None)), allowRemovals: false);

        await Apply(client, preview);

        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-9: Share must be kept without --allow-removals.");
        Expect.Equal(1, Assert.Single(client.AddRolePrivilegesCalls).Privileges.Count, "REQ-8: the other change (Delete) should still be added, on its own.");
    }

    // REQ-9: "...with the flag, removed."
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task PreviewTableSecurity_ALivePrivilegeTheFileSaysNone_WithAllowRemovals_IsPlannedForRemoval()
    {
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic));

        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(share: SecurityAccessLevel.None)), allowRemovals: true);

        var change = Single(preview);
        Expect.Equal(PrivilegeChangeAction.Remove, change.Action, "REQ-9: with --allow-removals the privilege held live but None in the file should be removed.");
        Expect.True(preview.HasChanges, "REQ-9: a removal is a write, so the file has something to import.");
    }

    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task ApplyTableSecurity_WithAllowRemovals_RemovesTheSharePrivilege()
    {
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic));
        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(share: SecurityAccessLevel.None)), allowRemovals: true);

        await Apply(client, preview);

        var (roleId, removed) = Assert.Single(client.RemoveRolePrivilegesCalls);
        Expect.Equal(HisUserId, roleId, "REQ-9: the privilege should be removed from HIS User.");
        var share = Assert.Single(removed);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Share, share.TableRight, "REQ-9: only Share should be removed.");
        Expect.Equal(Complaint, share.TableLogicalName ?? "", "REQ-9: Share should be removed on tn_complaint.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing was raised or lowered, so nothing should be added.");
    }

    // REQ-5: "Roles are identified by name... imports into an env where the role's id differs."
    // The same file imported into two environments writes to each environment's own role id.
    [Theory]
    [Trait("Requirement", "REQ-5")]
    [InlineData("b0000000-0000-0000-0000-00000000000a")]
    [InlineData("c0000000-0000-0000-0000-00000000000b")]
    public async Task ApplyTableSecurity_WritesToTheRoleFoundByNameInThisEnvironment(string environmentRoleId)
    {
        var roleId = Guid.Parse(environmentRoleId);
        var client = ImportEnvironment(roleId);
        var preview = await PreviewTable(client, ComplaintFile(HisUserRow(delete: SecurityAccessLevel.User)));

        await Apply(client, preview);

        Expect.Sequence(["HIS User"], client.RoleNameLookups.Distinct().ToList(), "REQ-5: the role should be looked up by its name.");
        Expect.Equal(roleId, Assert.Single(client.AddRolePrivilegesCalls).RoleId, "REQ-5: the write should go to this environment's id for 'HIS User'.");
    }

    // REQ-10: "A role named in the file that doesn't exist live fails that file's import with a clear message; roles are never created."
    // Example: "Role 'HIS Auditor' not found".
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task PreviewTableSecurity_ARoleThatDoesNotExistLive_FailsNamingTheRole()
    {
        var client = ImportEnvironment();

        var ex = await Assert.ThrowsAsync<RoleNotFoundException>(() => PreviewTable(client, ComplaintFile(HisUserRow(name: "HIS Auditor"))));

        Expect.Contains("Role 'HIS Auditor' not found", ex.Message, "REQ-10: the message should say which role was not found.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-10: nothing should be written, and no role created.");
    }

    // REQ-10: the whole file fails, so a role that does exist is not changed either.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task PreviewTableSecurity_OneMissingRoleFailsTheWholeFileSoNoRoleInItIsChanged()
    {
        var client = ImportEnvironment();

        await Assert.ThrowsAsync<RoleNotFoundException>(() => PreviewTable(client, ComplaintFile(HisUserRow(delete: SecurityAccessLevel.User), HisUserRow(name: "HIS Auditor"))));

        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-10: a file naming a role that does not exist fails as a whole; HIS User must not be changed.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-10: nothing should be removed either.");
    }

    // ---- Role files (REQ-18) ----

    // REQ-18: "Importing a role file diffs its non-table and outside-table privileges, prompts, and only removes with --allow-removals, like REQ-7-9."
    // Example: Add `prvBulkDelete: User` -> `+ prvBulkDelete: User`.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task PreviewRole_ANonTablePrivilegeTheRoleDoesNotHold_IsPlannedAsASetByName()
    {
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var preview = await PreviewRole(ImportEnvironment(), role);

        var change = Single(preview);
        Expect.Equal("prvBulkDelete", change.Privilege.PrivilegeName ?? "", "REQ-18: the new privilege should be identified by its platform name.");
        Expect.Equal(SecurityAccessLevel.None, change.From, "REQ-18: the role does not hold Bulk Delete live.");
        Expect.Equal(SecurityAccessLevel.User, change.To, "REQ-18: the file adds it at User.");
        Expect.Equal(PrivilegeChangeAction.Set, change.Action, "REQ-18: adding a privilege is a set (add-privilege) write.");
    }

    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task ApplyRole_ANonTablePrivilegeTheRoleDoesNotHold_AddsItByNameAtTheRightDepth()
    {
        var client = ImportEnvironment();
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));
        var preview = await PreviewRole(client, role);

        await Apply(client, preview);

        var (roleId, privileges) = Assert.Single(client.AddRolePrivilegesCalls);
        Expect.Equal(HisUserId, roleId, "REQ-18: the privilege should be added to HIS User.");
        var added = Assert.Single(privileges);
        Expect.Equal("prvBulkDelete", added.Privilege.PrivilegeName ?? "", "REQ-18: only the new privilege should be added, by name.");
        Expect.Equal(PrivilegeDepth.Basic, added.Depth, "REQ-18: User is depth Basic.");
    }

    // REQ-16/18: a privilege on a table outside the solution, listed under tables:, is diffed and written like a table privilege.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task ApplyRole_AnOutsideTablePrivilegeThatChanges_IsAddedAsATablePrivilege()
    {
        var client = ImportEnvironment();
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml(accountKeys: ["read: Organisation", "create: BusinessUnit"]));
        var preview = await PreviewRole(client, role);

        await Apply(client, preview);

        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal("account", added.Privilege.TableLogicalName ?? "", "REQ-18: the privilege is on account.");
        Expect.Equal<TablePrivilege?>(TablePrivilege.Create, added.Privilege.TableRight, "REQ-18: only Create on account is new.");
        Expect.Equal(PrivilegeDepth.Local, added.Depth, "REQ-18: BusinessUnit is depth Local.");
    }

    // REQ-18: "delete a line without the flag -> 'would remove (kept)'". A non-table privilege held live but missing from the file.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task PreviewRole_ANonTablePrivilegeHeldLiveButMissingFromTheFile_WithoutAllowRemovals_IsKeptAndReported()
    {
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml(privileges: []));

        var preview = await PreviewRole(ImportEnvironment(), role, allowRemovals: false);

        var change = Single(preview);
        Expect.Equal("prvExportToExcel", change.Privilege.PrivilegeName ?? "", "REQ-18: the privilege dropped from the file is prvExportToExcel.");
        Expect.Equal(PrivilegeChangeAction.KeptNotRemoved, change.Action, "REQ-18: without --allow-removals a line deleted from the file is kept and reported.");
        Expect.True(!preview.HasChanges, "REQ-18: a kept privilege is not a write.");
    }

    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task ApplyRole_ANonTablePrivilegeHeldLiveButMissingFromTheFile_WithAllowRemovals_RemovesIt()
    {
        var client = ImportEnvironment();
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml(privileges: []));
        var preview = await PreviewRole(client, role, allowRemovals: true);

        await Apply(client, preview);

        var (roleId, removed) = Assert.Single(client.RemoveRolePrivilegesCalls);
        Expect.Equal(HisUserId, roleId, "REQ-18: the privilege should be removed from HIS User.");
        Expect.Equal("prvExportToExcel", Assert.Single(removed).PrivilegeName ?? "", "REQ-18: only the privilege dropped from the file should be removed.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing was added or changed.");
    }

    // REQ-18: an outside-table privilege dropped from a table the file does list is a removal candidate too.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task PreviewRole_AnOutsideTablePrivilegeHeldLiveButNotListedForAListedTable_IsKeptWithoutAllowRemovals()
    {
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable("account", TablePrivilege.Create, PrivilegeDepth.Local));
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml());

        var preview = await PreviewRole(client, role, allowRemovals: false);

        var change = Single(preview);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Create, change.Privilege.TableRight, "REQ-18: Create on account is held live but not listed under the file's account.");
        Expect.Equal(PrivilegeChangeAction.KeptNotRemoved, change.Action, "REQ-18: without --allow-removals it is kept and reported.");
    }

    // REQ-17 / REQ-18: a role file never touches a table that is one of the solution's own, even with --allow-removals: those privileges belong to that table's security file.
    // HIS User holds tn_complaint privileges live (owned by tn_complaint.security.yml) and some on tn_lookupcode (also in the solution); the role file lists neither.
    [Fact]
    [Trait("Requirement", "REQ-17")]
    public async Task PreviewRole_NeverReportsOrRemovesPrivilegesOnATableInTheSolutionEvenWithAllowRemovals()
    {
        var client = ImportEnvironment(extraHisUserPrivileges: [OnTable(LookupCode, TablePrivilege.Read, PrivilegeDepth.Global), OnTable(LookupCode, TablePrivilege.Delete, PrivilegeDepth.Local)]);
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml());

        var preview = await PreviewRole(client, role, allowRemovals: true);

        Expect.Empty(Assert.Single(preview.Roles).Changes, "REQ-17/REQ-18: tn_complaint and tn_lookupcode are tables of the solution, so their privileges belong to their security files; the role file must not plan any change to them, even with --allow-removals.");
        Expect.True(!preview.HasChanges, "the role file matches the live role apart from tables it does not own, so there is nothing to import.");
    }

    // REQ-18: the solution's tables are resolved from Dataverse, by the solution named: "resolve the solution's tables from Dataverse via --solution".
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task PreviewRole_ResolvesTheSolutionsTablesFromDataverseByTheSolutionName()
    {
        var client = ImportEnvironment();
        var asked = new List<string>();
        client.SolutionEntityLogicalNames = solution => { asked.Add(solution); return [Complaint, LookupCode]; };

        await PreviewRole(client, RoleYamlDeserializer.FromYaml(HisUserRoleYaml()));

        Expect.Sequence([SolutionName], asked.Distinct().ToList(), "REQ-18: the solution's tables should be looked up in Dataverse for the solution the role file belongs to.");
    }

    // REQ-18: a solution that does not exist cannot say which tables are its own.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task PreviewRole_ASolutionThatDoesNotExist_Fails()
    {
        var client = ImportEnvironment();
        client.SolutionEntityLogicalNames = _ => null;

        await Assert.ThrowsAsync<SolutionNotFoundException>(() => PreviewRole(client, RoleYamlDeserializer.FromYaml(HisUserRoleYaml())));
    }

    // REQ-18: "A table held live but absent from `tables:` ... is reported as 'would remove ... (kept)'": the `account:` entry is deleted from the file.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task PreviewRole_AnOutsideTableHeldLiveButAbsentFromTheFile_WithoutAllowRemovals_IsKeptAndReported()
    {
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml(accountKeys: []));

        var preview = await PreviewRole(ImportEnvironment(), role, allowRemovals: false);

        var change = Single(preview);
        Expect.Equal("account", change.Privilege.TableLogicalName ?? "", "REQ-18: the privilege reported is on account, whose entry was deleted from tables:.");
        Expect.Equal<TablePrivilege?>(TablePrivilege.Read, change.Privilege.TableRight, "REQ-18: it is the Read on account the role holds live.");
        Expect.Equal(SecurityAccessLevel.Organisation, change.From, "REQ-18: Read on account is Organisation live.");
        Expect.Equal(SecurityAccessLevel.None, change.To, "REQ-18: the file no longer lists it.");
        Expect.Equal(PrivilegeChangeAction.KeptNotRemoved, change.Action, "REQ-18: without --allow-removals it is kept and reported.");
        Expect.True(!preview.HasChanges, "REQ-18: a kept privilege is not a write.");
    }

    // REQ-18: "...and only removed with --allow-removals."
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task ApplyRole_AnOutsideTableHeldLiveButAbsentFromTheFile_WithAllowRemovals_RemovesItsPrivileges()
    {
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable("account", TablePrivilege.Create, PrivilegeDepth.Local));
        var preview = await PreviewRole(client, RoleYamlDeserializer.FromYaml(HisUserRoleYaml(accountKeys: [])), allowRemovals: true);

        await Apply(client, preview);

        var (roleId, removed) = Assert.Single(client.RemoveRolePrivilegesCalls);
        Expect.Equal(HisUserId, roleId, "REQ-18: the privileges should be removed from HIS User.");
        Expect.Equal(2, removed.Count, "REQ-18: both of the role's privileges on account (Read and Create) should be removed.");
        Expect.True(removed.All(r => r.TableLogicalName == "account"), "REQ-18: only privileges on account should be removed; prvExportToExcel and the solution's tables are untouched.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing was added or changed.");
    }

    // REQ-18 / REQ-7: a role file that matches live has no changes.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task PreviewRole_FileMatchesLive_HasNoChanges()
    {
        var preview = await PreviewRole(ImportEnvironment(), RoleYamlDeserializer.FromYaml(HisUserRoleYaml()));

        Expect.True(!preview.HasChanges, "REQ-18: a role file that matches the live role has nothing to import.");
    }

    // REQ-18: lowering a non-table privilege's level is a set at the new depth.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task ApplyRole_ChangingANonTablePrivilegesLevel_AddsItAtTheNewDepth()
    {
        var client = ImportEnvironment();
        var role = RoleYamlDeserializer.FromYaml(HisUserRoleYaml(privileges: ["prvExportToExcel: ParentChildBusinessUnit"]));
        var preview = await PreviewRole(client, role);

        await Apply(client, preview);

        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal("prvExportToExcel", added.Privilege.PrivilegeName ?? "", "REQ-18: the privilege whose level changed should be re-added.");
        Expect.Equal(PrivilegeDepth.Deep, added.Depth, "REQ-18: ParentChildBusinessUnit is depth Deep.");
    }

    // REQ-10: a role file for a role that does not exist fails with the same clear message; roles are never created.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task PreviewRole_ARoleThatDoesNotExistLive_FailsNamingTheRole()
    {
        var client = ImportEnvironment();
        var role = new RoleDefinition { Name = "HIS Auditor", Privileges = new Dictionary<string, SecurityAccessLevel> { ["prvBulkDelete"] = SecurityAccessLevel.User } };

        var ex = await Assert.ThrowsAsync<RoleNotFoundException>(() => PreviewRole(client, role));

        Expect.Contains("Role 'HIS Auditor' not found", ex.Message, "REQ-10: the message should say which role was not found.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-10: roles are never created, and nothing should be written.");
    }
}

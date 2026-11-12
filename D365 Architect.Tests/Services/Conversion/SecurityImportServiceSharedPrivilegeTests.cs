using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 review follow-up. Covers <see cref="SecurityImportService"/> for privileges that one id stands for on several tables (the
/// activity tables share <c>prvCreateActivity</c>), privileges resolved before anything is written, and role-file entries that would
/// change a table of the solution. The fake models a shared privilege through <see cref="FakeDataverseClient.SharedPrivileges"/>.
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityImportServiceSharedPrivilegeTests
{
    private static readonly Guid CreateActivity = Guid.Parse("c0000000-0000-0000-0000-0000000000ac");

    /// <summary>Create on tn_complaint (in the solution) shares one privilege with Create on email and appointment (outside it).</summary>
    private static FakeDataverseClient WithSharedCreate(FakeDataverseClient client)
    {
        client.SharedPrivileges.Add((CreateActivity, "prvCreateActivity",
            [new TablePrivilegeRef(Complaint, TablePrivilege.Create), new TablePrivilegeRef("email", TablePrivilege.Create), new TablePrivilegeRef("appointment", TablePrivilege.Create)]));
        return client;
    }

    private static TableRoleSecurity Row(string name = "HIS User", SecurityAccessLevel create = SecurityAccessLevel.User, SecurityAccessLevel assign = SecurityAccessLevel.None, SecurityAccessLevel delete = SecurityAccessLevel.None) => new()
    {
        Name = name,
        Create = create,
        Read = SecurityAccessLevel.Organisation,
        Write = SecurityAccessLevel.BusinessUnit,
        Delete = delete,
        Append = SecurityAccessLevel.User,
        AppendTo = SecurityAccessLevel.Organisation,
        Assign = assign,
        Share = SecurityAccessLevel.None,
    };

    private static Task<SecurityImportPreview> PreviewTable(FakeDataverseClient client, params TableRoleSecurity[] rows) =>
        new SecurityImportService(client).PreviewTableSecurityAsync(EnvironmentUrl, "token", new TableSecurityDefinition { Table = Complaint, Roles = rows }, allowRemovals: true, CancellationToken.None);

    private static Task<SecurityImportPreview> PreviewRole(FakeDataverseClient client, string yaml, bool allowRemovals = true) =>
        new SecurityImportService(client).PreviewRoleAsync(EnvironmentUrl, "token", RoleYamlDeserializer.FromYaml(yaml), SolutionName, allowRemovals, CancellationToken.None);

    // A: a change to a shared privilege names every other table it changes.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task PreviewTableSecurity_ASharedPrivilege_ListsEveryOtherTableItAlsoChanges()
    {
        var client = WithSharedCreate(ImportEnvironment());

        var preview = await PreviewTable(client, Row(create: SecurityAccessLevel.Organisation));

        var change = Assert.Single(Assert.Single(preview.Roles).Changes);
        Expect.Equal(CreateActivity, change.PrivilegeId, "the change should carry the shared privilege's id.");
        Expect.Sequence(["appointment", "email"], change.AlsoChanges.ToList(), "the change should name every other table the shared privilege stands for.");
    }

    // A: one file naming the same role twice with different levels disagrees with itself.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task PreviewTableSecurity_TheSameRoleListedTwiceWithDifferentLevels_IsAConflict()
    {
        var client = ImportEnvironment();

        var ex = await Assert.ThrowsAsync<SecurityConflictException>(() => PreviewTable(client, Row(delete: SecurityAccessLevel.User), Row(delete: SecurityAccessLevel.Organisation)));

        Expect.Contains("HIS User", ex.Message, "the conflict should name the role.");
        Expect.Contains("prvDeletetn_complaint", ex.Message, "the conflict should name the privilege.");
    }

    // A: two outside tables sharing one privilege, at different levels, in one role file.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task PreviewRole_TwoOutsideTablesSharingAPrivilegeWithDifferentLevels_IsAConflict()
    {
        var client = ImportEnvironment();
        client.SharedPrivileges.Add((CreateActivity, "prvCreateActivity", [new TablePrivilegeRef("email", TablePrivilege.Create), new TablePrivilegeRef("appointment", TablePrivilege.Create)]));

        var ex = await Assert.ThrowsAsync<SecurityConflictException>(() => PreviewRole(client,
            "name: HIS User\nprivileges:\n  prvExportToExcel: Organisation\ntables:\n  account:\n    read: Organisation\n  email:\n    create: User\n  appointment:\n    create: Organisation\n"));

        Expect.Contains("prvCreateActivity", ex.Message, "the conflict should name the shared privilege.");
        Expect.Contains("email", ex.Message, "the conflict should name each table that disagrees.");
        Expect.Contains("appointment", ex.Message, "the conflict should name each table that disagrees.");
    }

    // A: agreeing entries for one shared privilege are one write.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task ApplyRole_TwoOutsideTablesSharingAPrivilegeAtTheSameLevel_WritesItOnce()
    {
        var client = ImportEnvironment();
        client.SharedPrivileges.Add((CreateActivity, "prvCreateActivity", [new TablePrivilegeRef("email", TablePrivilege.Create), new TablePrivilegeRef("appointment", TablePrivilege.Create)]));
        var service = new SecurityImportService(client);
        var preview = await PreviewRole(client,
            "name: HIS User\nprivileges:\n  prvExportToExcel: Organisation\ntables:\n  account:\n    read: Organisation\n  email:\n    create: User\n  appointment:\n    create: User\n");

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        Expect.Equal(1, Assert.Single(client.AddRolePrivilegesCalls).Privileges.Count, "one shared privilege is one write, however many tables list it.");
    }

    // A: a role file entry on an outside table whose privilege is shared with a table of the solution would change that table: refused.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task PreviewRole_AnOutsideTableEntrySharingAPrivilegeWithASolutionTable_IsRefused()
    {
        var client = WithSharedCreate(ImportEnvironment(extraHisUserPrivileges: [OnTable("email", TablePrivilege.Create, PrivilegeDepth.Basic), OnTable("appointment", TablePrivilege.Create, PrivilegeDepth.Basic)]));

        var ex = await Assert.ThrowsAsync<SecurityImportRefusedException>(() => PreviewRole(client,
            "name: HIS User\nprivileges:\n  prvExportToExcel: Organisation\ntables:\n  account:\n    read: Organisation\n  email:\n    create: Organisation\n  appointment:\n    create: Organisation\n"));

        Expect.Contains(Complaint, ex.Message, "the refusal should name the solution table the change would reach.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing should be written.");
    }

    // A: a shared privilege that also covers a table of the solution is that table's file's business, so a role file leaving it out never removes it.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task PreviewRole_ASharedPrivilegeAlsoOnASolutionTable_IsNotARemovalCandidate()
    {
        var client = WithSharedCreate(ImportEnvironment(extraHisUserPrivileges: [OnTable("email", TablePrivilege.Create, PrivilegeDepth.Basic), OnTable("appointment", TablePrivilege.Create, PrivilegeDepth.Basic)]));

        var preview = await PreviewRole(client, HisUserRoleYaml(), allowRemovals: true);

        Expect.Empty(Assert.Single(preview.Roles).Changes, "Create on email/appointment is the same privilege as Create on tn_complaint, owned by its security file: the role file must not plan to remove it.");
    }

    // 4: a privilege the table doesn't have (Assign on an organisation-owned table) fails the whole file before anything is written.
    [Fact]
    [Trait("Requirement", "REVIEW-4")]
    public async Task PreviewTableSecurity_APrivilegeTheTableDoesNotHave_FailsTheWholeFile()
    {
        var client = ImportEnvironment();
        client.MissingTablePrivileges.Add((Complaint, TablePrivilege.Assign));

        var ex = await Assert.ThrowsAsync<PrivilegeNotFoundException>(() => PreviewTable(client, Row(assign: SecurityAccessLevel.User, delete: SecurityAccessLevel.User)));

        Expect.Contains("Assign", ex.Message, "the error should name the right that doesn't exist.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing in the file (not even Delete) should be written.");
    }

    // 4: None for a privilege the table doesn't have is simply true, not an error.
    [Fact]
    [Trait("Requirement", "REVIEW-4")]
    public async Task PreviewTableSecurity_APrivilegeTheTableDoesNotHave_SetToNone_IsFine()
    {
        var client = ImportEnvironment();
        client.MissingTablePrivileges.Add((Complaint, TablePrivilege.Assign));

        var preview = await PreviewTable(client, Row(delete: SecurityAccessLevel.User));

        Expect.Equal<TablePrivilege?>(TablePrivilege.Delete, Assert.Single(Assert.Single(preview.Roles).Changes).Privilege.TableRight, "only Delete changes; Assign: None on a table without Assign is fine.");
    }

    // 4: an unknown non-table privilege in a role file fails the file during preview.
    [Fact]
    [Trait("Requirement", "REVIEW-4")]
    public async Task PreviewRole_AnUnknownNonTablePrivilege_FailsTheWholeFile()
    {
        var client = ImportEnvironment();
        client.UnknownPrivilegeNames.Add("prvTypo");

        var ex = await Assert.ThrowsAsync<PrivilegeNotFoundException>(() => PreviewRole(client, HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User", "prvTypo: User"])));

        Expect.Contains("prvTypo", ex.Message, "the error should name the privilege that doesn't exist.");
    }

    // 5: a role file's privileges: entry that is a table privilege of a solution table is refused.
    [Fact]
    [Trait("Requirement", "REVIEW-5")]
    public async Task PreviewRole_APrivilegesEntryThatIsATablePrivilegeOfASolutionTable_IsRefused()
    {
        var client = ImportEnvironment();

        var ex = await Assert.ThrowsAsync<SecurityImportRefusedException>(() => PreviewRole(client, HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvWritetn_complaint: Organisation"])));

        Expect.Contains("prvWritetn_complaint", ex.Message, "the refusal should name the privilege.");
        Expect.Contains(Complaint, ex.Message, "the refusal should name the solution table it belongs to.");
    }
}

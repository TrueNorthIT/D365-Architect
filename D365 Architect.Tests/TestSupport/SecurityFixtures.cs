using D365Architect.Services.Dataverse;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// The security-role data the issue #21 tests share, so each test reads as a statement about one requirement rather than a page of setup.
/// The story is the one in the requirements: solution <c>qad</c> holds the roles "HIS User" and "HIS Super User" and the table
/// <c>tn_complaint</c> (and <c>tn_lookupcode</c>, which no role can reach); "System Administrator" exists but is not in the solution.
/// Ids are different in every "environment" a test builds, because a role is found by name, never by id.
/// </summary>
internal static class SecurityFixtures
{
    public const string SolutionName = "qad";
    public const string Complaint = "tn_complaint";
    public const string LookupCode = "tn_lookupcode";

    public static readonly Guid HisUserId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid HisSuperUserId = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    public static readonly Guid SystemAdministratorId = Guid.Parse("a0000000-0000-0000-0000-000000000003");

    public static SecurityRole HisUser(Guid? id = null) => new(id ?? HisUserId, "HIS User");

    public static SecurityRole HisSuperUser(Guid? id = null) => new(id ?? HisSuperUserId, "HIS Super User");

    public static SecurityRole SystemAdministrator() => new(SystemAdministratorId, "System Administrator");

    /// <summary>A privilege on a table, as <see cref="IDataverseClient.GetRolePrivilegesAsync"/> returns it (platform name filled in).</summary>
    public static RolePrivilege OnTable(string table, TablePrivilege right, PrivilegeDepth depth) =>
        new(PrivilegeIdentity.ForTable(table, right, privilegeName: $"prv{right}{table}"), depth);

    /// <summary>A non-table privilege, as <see cref="IDataverseClient.GetRolePrivilegesAsync"/> returns it.</summary>
    public static RolePrivilege Named(string privilegeName, PrivilegeDepth depth) => new(PrivilegeIdentity.ForName(privilegeName), depth);

    /// <summary>
    /// "HIS User on tn_complaint" from the requirements: Create User, Read Organisation, Write BusinessUnit, Delete None, Append User,
    /// AppendTo Organisation, Assign None, Share None. Delete, Assign and Share are not held, so they are not in the list.
    /// </summary>
    public static List<RolePrivilege> HisUserOnComplaint() =>
    [
        OnTable(Complaint, TablePrivilege.Create, PrivilegeDepth.Basic),
        OnTable(Complaint, TablePrivilege.Read, PrivilegeDepth.Global),
        OnTable(Complaint, TablePrivilege.Write, PrivilegeDepth.Local),
        OnTable(Complaint, TablePrivilege.Append, PrivilegeDepth.Basic),
        OnTable(Complaint, TablePrivilege.AppendTo, PrivilegeDepth.Global),
    ];

    /// <summary>All eight privileges on a table at one depth.</summary>
    public static List<RolePrivilege> AllEightOn(string table, PrivilegeDepth depth) =>
        Enum.GetValues<TablePrivilege>().Select(right => OnTable(table, right, depth)).ToList();

    /// <summary>
    /// A fake environment for the export tests: solution <c>qad</c> with tables tn_complaint and tn_lookupcode and roles HIS User and
    /// HIS Super User; System Administrator exists with full access but is not in the solution (reading its privileges fails the test).
    /// HIS User: the tn_complaint rights above, Read on account (outside the solution), and the non-table privileges prvExportToExcel and prvGoMobile.
    /// HIS Super User: all eight on tn_complaint at Organisation.
    /// </summary>
    public static FakeDataverseClient ExportEnvironment()
    {
        var client = new FakeDataverseClient
        {
            SolutionEntityLogicalNames = _ => [Complaint, LookupCode],
            SolutionRoles = _ => [HisUser(), HisSuperUser()],
        };
        client.LiveRoles.AddRange([HisUser(), HisSuperUser(), SystemAdministrator()]);
        client.RolePrivileges[HisUserId] = [.. HisUserOnComplaint(),
            OnTable("account", TablePrivilege.Read, PrivilegeDepth.Global),
            Named("prvExportToExcel", PrivilegeDepth.Global),
            Named("prvGoMobile", PrivilegeDepth.Global)];
        client.RolePrivileges[HisSuperUserId] = AllEightOn(Complaint, PrivilegeDepth.Global);
        return client;
    }

    /// <summary>
    /// A fake environment for the import tests: the role "HIS User" (under <paramref name="roleId"/>) holds <see cref="HisUserOnComplaint"/>
    /// plus prvExportToExcel at Organisation and Read on account (a table outside the solution) at Organisation, so a test can tell untouched privileges from changed ones. Solution qad has the tables tn_complaint and tn_lookupcode and the role HIS User.
    /// </summary>
    public static FakeDataverseClient ImportEnvironment(Guid? roleId = null, params RolePrivilege[] extraHisUserPrivileges)
    {
        var id = roleId ?? HisUserId;
        var client = new FakeDataverseClient
        {
            SolutionEntityLogicalNames = _ => [Complaint, LookupCode],
            SolutionRoles = _ => [HisUser(id)],
        };
        client.LiveRoles.Add(HisUser(id));
        client.RolePrivileges[id] = [.. HisUserOnComplaint(),
            OnTable("account", TablePrivilege.Read, PrivilegeDepth.Global),
            Named("prvExportToExcel", PrivilegeDepth.Global),
            .. extraHisUserPrivileges];
        return client;
    }

    public static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    /// <summary>The <c>tn_complaint.security.yml</c> text for HIS User as the live role stands (Delete None), with the given overrides of the eight keys.</summary>
    public static string ComplaintFileYaml(string create = "User", string read = "Organisation", string write = "BusinessUnit", string delete = "None", string append = "User", string appendTo = "Organisation", string assign = "None", string share = "None", string role = "HIS User") =>
        $"""
        table: tn_complaint
        roles:
          - name: {role}
            create: {create}
            read: {read}
            write: {write}
            delete: {delete}
            append: {append}
            appendTo: {appendTo}
            assign: {assign}
            share: {share}
        """;

    /// <summary>
    /// The <c>his-user.role.yml</c> text as the live role stands by default: prvExportToExcel at Organisation, and Read on account at Organisation.
    /// Pass lines to replace either list (e.g. <c>privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]</c>).
    /// </summary>
    public static string HisUserRoleYaml(string[]? privileges = null, string[]? accountKeys = null)
    {
        privileges ??= ["prvExportToExcel: Organisation"];
        accountKeys ??= ["read: Organisation"];
        var lines = new List<string> { "name: HIS User" };
        if (privileges.Length > 0)
        {
            lines.Add("privileges:");
            lines.AddRange(privileges.Select(p => $"  {p}"));
        }

        if (accountKeys.Length > 0)
        {
            lines.Add("tables:");
            lines.Add("  account:");
            lines.AddRange(accountKeys.Select(k => $"    {k}"));
        }

        return string.Join("\n", lines) + "\n";
    }
}

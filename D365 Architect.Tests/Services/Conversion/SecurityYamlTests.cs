using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;
using YamlDotNet.Core;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 (security role privileges as YAML). Covers the two file formats and their serializers/deserializers:
/// <see cref="TableSecurityYamlSerializer"/>/<see cref="TableSecurityYamlDeserializer"/> for <c>&lt;table&gt;.security.yml</c> and
/// <see cref="RoleYamlSerializer"/>/<see cref="RoleYamlDeserializer"/> for <c>&lt;role-name&gt;.role.yml</c>. The formats are frozen (see the
/// requirements): keys are camelCase, a role is identified by NAME, dictionary keys (privilege names, table names) are written as they are.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityYamlTests
{
    private static TableRoleSecurity HisUserOnComplaint() => new()
    {
        Name = "HIS User",
        Create = SecurityAccessLevel.User,
        Read = SecurityAccessLevel.Organisation,
        Write = SecurityAccessLevel.BusinessUnit,
        Delete = SecurityAccessLevel.None,
        Append = SecurityAccessLevel.User,
        AppendTo = SecurityAccessLevel.Organisation,
        Assign = SecurityAccessLevel.None,
        Share = SecurityAccessLevel.None,
    };

    // REQ-2: "Each role in the file shows all 8 privileges with their access level." Even the ones that are None are written.
    // Code path: TableSecurityYamlSerializer.ToYaml.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public void TableSecurityToYaml_WritesAllEightPrivilegesForARoleIncludingNone()
    {
        var yaml = TableSecurityYamlSerializer.ToYaml(new TableSecurityDefinition { Table = "tn_complaint", Roles = [HisUserOnComplaint()] });

        foreach (var expected in new[] { "table: tn_complaint", "name: HIS User", "create: User", "read: Organisation", "write: BusinessUnit", "delete: None", "append: User", "appendTo: Organisation", "assign: None", "share: None" })
        {
            Expect.Contains(expected, yaml, $"REQ-2: every one of the 8 privileges should be written for a role, with its access level (and the table and role name). Missing '{expected}'.");
        }
    }

    // REQ-5: "Roles are identified by name, so the file works across dev/test/prod." The role row has a name and no id of any kind.
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public void TableSecurityToYaml_IdentifiesTheRoleByNameAndWritesNoId()
    {
        var yaml = TableSecurityYamlSerializer.ToYaml(new TableSecurityDefinition { Table = "tn_complaint", Roles = [HisUserOnComplaint()] });

        Expect.Contains("- name: HIS User", yaml, "REQ-5: the role should be identified by its name.");
        Expect.True(!System.Text.RegularExpressions.Regex.IsMatch(yaml, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-") && !yaml.Contains("roleid", StringComparison.OrdinalIgnoreCase),
            $"REQ-5: no role id (a GUID) should be written, so the file works in an environment where the role's id differs. Actual: <{yaml}>.");
    }

    // The frozen format: the keys come in the documented order.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public void TableSecurityToYaml_WritesTheKeysInTheDocumentedOrder()
    {
        var yaml = TableSecurityYamlSerializer.ToYaml(new TableSecurityDefinition { Table = "tn_complaint", Roles = [HisUserOnComplaint()] });

        var keys = yaml.Split('\n').Select(l => l.Trim().TrimStart('-').Trim()).Where(l => l.Contains(':')).Select(l => l[..l.IndexOf(':')]).ToList();

        Expect.Sequence(["table", "roles", "name", "create", "read", "write", "delete", "append", "appendTo", "assign", "share"], keys, "REQ-2: the table security file's keys should be in the documented order.");
    }

    // Round trip: what is written can be read back unchanged.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public void TableSecurity_RoundTrips()
    {
        var original = new TableSecurityDefinition { Table = "tn_complaint", Roles = [HisUserOnComplaint(), new TableRoleSecurity { Name = "HIS Super User", Delete = SecurityAccessLevel.ParentChildBusinessUnit, Share = SecurityAccessLevel.User }] };

        var read = TableSecurityYamlDeserializer.FromYaml(TableSecurityYamlSerializer.ToYaml(original));

        Expect.Equal("tn_complaint", read.Table, "the table should survive a round trip.");
        Expect.Equal(2, read.Roles.Count, "both roles should survive a round trip.");
        Expect.Equal(SecurityAccessLevel.Organisation, read.Roles[0].AppendTo, "HIS User's AppendTo should survive a round trip.");
        Expect.Equal(SecurityAccessLevel.None, read.Roles[0].Delete, "HIS User's Delete (None) should survive a round trip.");
        Expect.Equal("HIS Super User", read.Roles[1].Name, "the second role's name should survive a round trip.");
        Expect.Equal(SecurityAccessLevel.ParentChildBusinessUnit, read.Roles[1].Delete, "HIS Super User's Delete (ParentChildBusinessUnit) should survive a round trip.");
        Expect.Equal(SecurityAccessLevel.User, read.Roles[1].Share, "HIS Super User's Share should survive a round trip.");
    }

    // The frozen example from the requirements reads as written.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public void TableSecurityFromYaml_ReadsTheDocumentedExample()
    {
        var read = TableSecurityYamlDeserializer.FromYaml(SecurityFixtures.ComplaintFileYaml(create: "User", read: "Organisation", write: "BusinessUnit", delete: "None", append: "User", appendTo: "Organisation"));

        var role = Assert.Single(read.Roles);
        Expect.Equal("HIS User", role.Name, "the role name should be read.");
        Expect.Equal(SecurityAccessLevel.User, role.Create, "create: User should be read.");
        Expect.Equal(SecurityAccessLevel.Organisation, role.Read, "read: Organisation should be read.");
        Expect.Equal(SecurityAccessLevel.BusinessUnit, role.Write, "write: BusinessUnit should be read.");
        Expect.Equal(SecurityAccessLevel.None, role.Delete, "delete: None should be read.");
        Expect.Equal(SecurityAccessLevel.Organisation, role.AppendTo, "appendTo: Organisation should be read.");
    }

    // REQ-9: "A privilege held live but missing / None in the file". A key left out of a role row reads as None (not held).
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public void TableSecurityFromYaml_AKeyLeftOutReadsAsNone()
    {
        var read = TableSecurityYamlDeserializer.FromYaml("table: tn_complaint\nroles:\n  - name: HIS User\n    read: User\n");

        var role = Assert.Single(read.Roles);
        Expect.Equal(SecurityAccessLevel.User, role.Read, "the key that is given should be read.");
        Expect.Equal(SecurityAccessLevel.None, role.Share, "a privilege key left out of the file should read as None (not held).");
    }

    // REQ-21: "VS Code flags `delete: Everyone` as invalid." The tool itself also refuses a value that is not one of the five levels.
    [Fact]
    [Trait("Requirement", "REQ-21")]
    public void TableSecurityFromYaml_AnAccessLevelThatIsNotOneOfTheFiveIsRejected()
    {
        Assert.Throws<YamlException>(() => TableSecurityYamlDeserializer.FromYaml(SecurityFixtures.ComplaintFileYaml(delete: "Everyone")));
    }

    // REQ-14/15/16: the role file format, exactly as in the requirements: only held privileges, by platform name, then the tables outside the solution.
    // Code path: RoleYamlSerializer.ToYaml.
    [Fact]
    [Trait("Requirement", "REQ-14")]
    public void RoleToYaml_WritesNonTablePrivilegesByPlatformNameWithTheirAccessLevel()
    {
        var yaml = RoleYamlSerializer.ToYaml(new RoleDefinition
        {
            Name = "HIS User",
            Privileges = new Dictionary<string, SecurityAccessLevel> { ["prvExportToExcel"] = SecurityAccessLevel.Organisation, ["prvGoMobile"] = SecurityAccessLevel.Organisation },
        });

        Expect.Contains("name: HIS User", yaml, "REQ-14: the role file should name the role.");
        Expect.Contains("privileges:", yaml, "REQ-14: the role file should have a privileges section.");
        Expect.Contains("prvExportToExcel: Organisation", yaml, "REQ-14: a non-table privilege should be written by its platform name (not re-cased) with its access level.");
        Expect.Contains("prvGoMobile: Organisation", yaml, "REQ-14: every non-table privilege held should be written.");
    }

    // REQ-16: "Privileges on tables outside the solution go in the role file under `tables:` with the same 8 privilege keys." `tables: account: { read: Organisation }`
    // REQ-15: only what is held is written, so a privilege that is not set has no line.
    [Fact]
    [Trait("Requirement", "REQ-16")]
    public void RoleToYaml_WritesOutsideTablePrivilegesUnderTablesAndOnlyTheOnesHeld()
    {
        var yaml = RoleYamlSerializer.ToYaml(new RoleDefinition
        {
            Name = "HIS User",
            Tables = new Dictionary<string, TablePrivilegeLevels> { ["account"] = new() { Read = SecurityAccessLevel.Organisation }, ["systemuser"] = new() { Read = SecurityAccessLevel.Organisation, AppendTo = SecurityAccessLevel.User } },
        });

        Expect.Contains("tables:", yaml, "REQ-16: privileges on tables outside the solution should go under tables:.");
        Expect.Contains("account:", yaml, "REQ-16: the table's logical name should be a key under tables:.");
        Expect.Contains("read: Organisation", yaml, "REQ-16: a held table privilege should be written with the same camelCase keys as a table file.");
        Expect.Contains("appendTo: User", yaml, "REQ-16: the AppendTo privilege uses the key appendTo.");
        Expect.True(!yaml.Contains("create:", StringComparison.Ordinal) && !yaml.Contains("delete:", StringComparison.Ordinal) && !yaml.Contains("share:", StringComparison.Ordinal),
            $"REQ-15: a privilege that is not held should have no line. Actual: <{yaml}>.");
    }

    // REQ-15: a role that holds nothing outside a table file is just its name: no empty sections.
    [Fact]
    [Trait("Requirement", "REQ-15")]
    public void RoleToYaml_ARoleWithNoPrivilegesIsJustItsName()
    {
        var yaml = RoleYamlSerializer.ToYaml(new RoleDefinition { Name = "HIS Auditor" });

        Expect.Contains("name: HIS Auditor", yaml, "REQ-13: a role with nothing else to list still has its file, naming the role.");
        Expect.True(!yaml.Contains("privileges", StringComparison.Ordinal) && !yaml.Contains("tables", StringComparison.Ordinal),
            $"REQ-15: absent means not held, so a role holding no privileges should have no privileges or tables section. Actual: <{yaml}>.");
    }

    // The frozen role example reads as written, and the keys of the dictionaries keep their spelling.
    [Fact]
    [Trait("Requirement", "REQ-14")]
    public void RoleFromYaml_ReadsTheDocumentedExample()
    {
        var read = RoleYamlDeserializer.FromYaml(
            """
            name: HIS User
            privileges:
              prvExportToExcel: Organisation
              prvGoMobile: Organisation
            tables:
              account:
                read: Organisation
              systemuser:
                read: Organisation
            """);

        Expect.Equal("HIS User", read.Name, "the role name should be read.");
        Expect.Equal(SecurityAccessLevel.Organisation, read.Privileges!["prvExportToExcel"], "prvExportToExcel: Organisation should be read, with the key spelt as written.");
        Expect.Equal(2, read.Privileges.Count, "both non-table privileges should be read.");
        Expect.Equal<SecurityAccessLevel?>(SecurityAccessLevel.Organisation, read.Tables!["account"].Read, "tables: account: read: Organisation should be read.");
        Expect.Equal<SecurityAccessLevel?>(null, read.Tables["account"].Delete, "a table privilege that is not listed should read as not held (null).");
        Expect.Equal(2, read.Tables.Count, "both outside tables should be read.");
    }

    [Fact]
    [Trait("Requirement", "REQ-14")]
    public void Role_RoundTrips()
    {
        var original = new RoleDefinition
        {
            Name = "HIS User",
            Privileges = new Dictionary<string, SecurityAccessLevel> { ["prvBulkDelete"] = SecurityAccessLevel.User },
            Tables = new Dictionary<string, TablePrivilegeLevels> { ["account"] = new() { Read = SecurityAccessLevel.ParentChildBusinessUnit } },
        };

        var read = RoleYamlDeserializer.FromYaml(RoleYamlSerializer.ToYaml(original));

        Expect.Equal("HIS User", read.Name, "the role name should survive a round trip.");
        Expect.Equal(SecurityAccessLevel.User, read.Privileges!["prvBulkDelete"], "a non-table privilege should survive a round trip.");
        Expect.Equal<SecurityAccessLevel?>(SecurityAccessLevel.ParentChildBusinessUnit, read.Tables!["account"].Read, "an outside-table privilege should survive a round trip.");
    }

    [Fact]
    [Trait("Requirement", "REQ-21")]
    public void RoleFromYaml_AnAccessLevelThatIsNotOneOfTheFiveIsRejected()
    {
        Assert.Throws<YamlException>(() => RoleYamlDeserializer.FromYaml("name: HIS User\nprivileges:\n  prvBulkDelete: Everyone\n"));
    }
}

using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Commands.Security;

/// <summary>
/// Issue #21 (security role privileges as YAML). <c>security export --solution x [--output dir] [--table t]... [--role r]...</c> through the real
/// command tree (<see cref="SecurityCliHost"/>), the real <see cref="SecurityExportService"/> and a fake Dataverse
/// (<see cref="SecurityFixtures.ExportEnvironment"/>): which files are written, and where. The layout is the one <c>solution export</c> writes:
/// <c>&lt;output&gt;/&lt;solution&gt;/&lt;table&gt;/&lt;table&gt;.security.yml</c> and <c>&lt;output&gt;/&lt;solution&gt;/roles/&lt;role-name&gt;.role.yml</c>.
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "security-roles-yaml")]
public sealed class ExportSecurityCommandTests : IDisposable
{
    private readonly TempDirectory _output = new();

    public void Dispose() => _output.Dispose();

    private string TableFile(string table) => Path.Combine(_output.Path, SolutionName, table, $"{table}.security.yml");

    private string RoleFile(string stem) => Path.Combine(_output.Path, SolutionName, "roles", $"{stem}.role.yml");

    private Task<int> Run(ConsoleHarness console, SecurityCliHost host, params string[] extraArgs) =>
        host.RunAsync(console, ["security", "export", "--solution", SolutionName, "--output", _output.Path, .. extraArgs]);

    private List<string> FilesWritten() =>
        Directory.Exists(_output.Path)
            ? Directory.GetFiles(_output.Path, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(_output.Path, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList()
            : [];

    // REQ-6: "`security export --solution x` writes every table security file and every role file in the same layout as `solution export`."
    // Code path: ExportSecurityCommand (security export) -> ISecurityExportService.ExportSecurityAsync, then the files under <output>/<solution>/.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task SecurityExport_WritesEveryTableSecurityFileAndEveryRoleFileInTheSolutionExportLayout()
    {
        using var console = new ConsoleHarness();

        var exitCode = await Run(console, new SecurityCliHost { Client = ExportEnvironment() });

        Expect.Equal(0, exitCode, $"REQ-6: security export should succeed. Output: <{console.Output}>");
        Expect.Sequence(
            ["qad/roles/his-super-user.role.yml", "qad/roles/his-user.role.yml", "qad/tn_complaint/tn_complaint.security.yml"],
            FilesWritten(),
            "REQ-6: one security file in tn_complaint's folder and one role file per role under roles/ - and none for tn_lookupcode (REQ-4).");
    }

    // REQ-1/REQ-2: the table file written lists the roles with all eight privileges (what was written to disk is the real file, not just a path).
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public async Task SecurityExport_TheTableSecurityFileListsEachRoleWithItsPrivileges()
    {
        using var console = new ConsoleHarness();

        await Run(console, new SecurityCliHost { Client = ExportEnvironment() });

        var file = TableSecurityYamlDeserializer.FromYaml(File.ReadAllText(TableFile(Complaint)));
        var hisUser = file.Roles.Single(r => r.Name == "HIS User");
        Expect.Equal(SecurityAccessLevel.Organisation, hisUser.Read, "REQ-2: the file on disk should show HIS User's Read on tn_complaint as Organisation.");
        Expect.Equal(SecurityAccessLevel.None, hisUser.Delete, "REQ-2: and Delete as None.");
    }

    // REQ-6: "optional repeatable `--table` limits to those tables' security files." Example: --table tn_complaint writes only tn_complaint/tn_complaint.security.yml.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task SecurityExport_LimitedToATable_WritesOnlyThatTablesSecurityFile()
    {
        using var console = new ConsoleHarness();

        var exitCode = await Run(console, new SecurityCliHost { Client = ExportEnvironment() }, "--table", "tn_complaint");

        Expect.Equal(0, exitCode, $"REQ-6: security export --table should succeed. Output: <{console.Output}>");
        Expect.Sequence(["qad/tn_complaint/tn_complaint.security.yml"], FilesWritten(), "REQ-6/REQ-19: --table should write only that table's security file and no role files.");
    }

    // REQ-6: the option is repeatable. A second table with access gets its file too.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task SecurityExport_TableOptionIsRepeatable()
    {
        using var console = new ConsoleHarness();
        var client = ExportEnvironment();
        client.RolePrivileges[HisSuperUserId] = [.. AllEightOn(Complaint, PrivilegeDepth.Global), .. AllEightOn(LookupCode, PrivilegeDepth.Global)];

        var exitCode = await Run(console, new SecurityCliHost { Client = client }, "--table", "tn_complaint", "--table", "tn_lookupcode");

        Expect.Equal(0, exitCode, $"REQ-6: repeating --table should succeed. Output: <{console.Output}>");
        Expect.Sequence(["qad/tn_complaint/tn_complaint.security.yml", "qad/tn_lookupcode/tn_lookupcode.security.yml"], FilesWritten(), "REQ-6: both tables named should get their security file.");
    }

    // REQ-19: "`security export --solution qad --role "HIS User"` writes only `roles/his-user.role.yml`."
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public async Task SecurityExport_LimitedToARole_WritesOnlyThatRolesFile()
    {
        using var console = new ConsoleHarness();

        var exitCode = await Run(console, new SecurityCliHost { Client = ExportEnvironment() }, "--role", "HIS User");

        Expect.Equal(0, exitCode, $"REQ-19: security export --role should succeed. Output: <{console.Output}>");
        Expect.Sequence(["qad/roles/his-user.role.yml"], FilesWritten(), "REQ-19: --role should write only that role's file and no table security files.");
    }

    // REQ-19: the role file written is the real one: non-table privileges by platform name (REQ-14) and outside tables (REQ-16).
    [Fact]
    [Trait("Requirement", "REQ-14")]
    public async Task SecurityExport_TheRoleFileListsTheNonTableAndOutsideTablePrivilegesTheRoleHolds()
    {
        using var console = new ConsoleHarness();

        await Run(console, new SecurityCliHost { Client = ExportEnvironment() }, "--role", "HIS User");

        var role = RoleYamlDeserializer.FromYaml(File.ReadAllText(RoleFile("his-user")));
        Expect.Equal(SecurityAccessLevel.Organisation, role.Privileges!["prvExportToExcel"], "REQ-14: prvExportToExcel: Organisation should be in the file.");
        Expect.Equal<SecurityAccessLevel?>(SecurityAccessLevel.Organisation, role.Tables!["account"].Read, "REQ-16: account's Read should be under tables:.");
    }

    // REQ-20: "A `--table` not in the solution / with no folder fails with a clear error, as `solution export --table` does."
    // Example: "Table 'tn_typo' is not in solution 'qad'". Nothing is written.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task SecurityExport_ATableNotInTheSolution_FailsNamingItAndWritesNothing()
    {
        using var console = new ConsoleHarness();

        var exitCode = await Run(console, new SecurityCliHost { Client = ExportEnvironment() }, "--table", "tn_typo");

        Expect.True(exitCode != 0, $"REQ-20: a table that is not in the solution should be an error exit. Output: <{console.Output}>");
        Expect.Contains("tn_typo", console.Output, "REQ-20: the error should name the table.");
        Expect.Contains("qad", console.Output, "REQ-20: the error should name the solution.");
        Expect.Empty(FilesWritten(), "REQ-20: nothing should be written when the request is invalid.");
    }

    // As solution export: a solution that does not exist is a clear error.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task SecurityExport_ASolutionThatDoesNotExist_FailsAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var client = new FakeDataverseClient { SolutionEntityLogicalNames = _ => null, SolutionRoles = _ => null };

        var exitCode = await Run(console, new SecurityCliHost { Client = client });

        Expect.True(exitCode != 0, $"REQ-6: an unknown solution should be an error exit. Output: <{console.Output}>");
        Expect.Contains(SolutionName, console.Output, "REQ-6: the error should name the solution.");
        Expect.Empty(FilesWritten(), "REQ-6: nothing should be written for a solution that does not exist.");
    }

    // REQ-24: "`security export` ... with a `--role "X"` where no role named X is in the solution fails with a clear error containing the role name and
    // solution name (e.g. "Role 'X' is not in solution 'qad'"), non-zero exit, nothing written." Mirrors REQ-20.
    [Fact]
    [Trait("Requirement", "REQ-24")]
    public async Task SecurityExport_ARoleNotInTheSolution_FailsNamingTheRoleAndSolutionAndWritesNothing()
    {
        using var console = new ConsoleHarness();

        var exitCode = await Run(console, new SecurityCliHost { Client = ExportEnvironment() }, "--role", "HIS Auditor");

        Expect.True(exitCode != 0, $"REQ-24: a role that is not in the solution should be an error exit. Output: <{console.Output}>");
        Expect.Contains("HIS Auditor", console.Output, "REQ-24: the error should name the role.");
        Expect.Contains(SolutionName, console.Output, "REQ-24: the error should name the solution.");
        Expect.Empty(FilesWritten(), "REQ-24: nothing should be written when the request is invalid.");
    }
}

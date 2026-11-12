using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Commands.Security;

/// <summary>
/// Issue #21 (security role privileges as YAML), REQ-11: "`solution import` imports each table's security file after its forms, and role files
/// after all table folders; `--whatif`, `--yes`, `--table` behave as for other files; `--table` skips role files." Example: `solution import
/// --whatif` shows security and role diffs and writes nothing.
/// <c>solution import</c> through the real command tree (<see cref="SecurityCliHost"/>) over a folder holding just a table's security file and a
/// role file (so no table is created and no other stage is involved; the stage order itself is pinned by
/// <c>SolutionImportOrchestratorSecurityTests</c>), against a fake Dataverse (<see cref="SecurityFixtures.ImportEnvironment"/>).
/// HIS User lacks Delete on tn_complaint and prvBulkDelete, which the files add.
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "security-roles-yaml")]
public sealed class SolutionImportCommandSecurityTests : IDisposable
{
    private readonly TempDirectory _folder = new();

    public SolutionImportCommandSecurityTests()
    {
        Write("tn_complaint/tn_complaint.security.yml", ComplaintFileYaml(delete: "User"));
        Write("roles/his-user.role.yml", HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));
    }

    public void Dispose() => _folder.Dispose();

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_folder.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private Task<int> Run(ConsoleHarness console, FakeDataverseClient client, params string[] extraArgs) =>
        new SecurityCliHost { Client = client }.RunAsync(console, ["solution", "import", "--input", _folder.Path, "--solution", SolutionName, .. extraArgs]);

    // REQ-11: "`solution import --whatif` shows security and role diffs and writes nothing."
    // Code path: ImportSolutionCommand -> SolutionImportOrchestrator (ImportSecurity, ImportRole steps) -> SecurityImportService.Preview*.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task SolutionImport_WhatIf_ShowsTheSecurityAndRoleDiffsAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();

        var exitCode = await Run(console, client, "--whatif");

        Expect.Equal(0, exitCode, $"REQ-11: --whatif should succeed. Output: <{console.Output}>");
        Expect.Contains("Delete: None → User", console.Output, "REQ-11: the table security diff should be shown.");
        Expect.Contains("+ prvBulkDelete: User", console.Output, "REQ-11: the role diff should be shown.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-11: --whatif should write nothing.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-11: --whatif should remove nothing.");
    }

    // REQ-11: "`--yes` behave as for other files": no prompt, and both the table security file and the role file are imported.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task SolutionImport_Yes_ImportsTheSecurityFileAndTheRoleFileWithoutPrompting()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();

        var exitCode = await Run(console, client, "--yes");

        Expect.Equal(0, exitCode, $"REQ-11: --yes should import without asking. Output: <{console.Output}>");
        var added = client.AddRolePrivilegesCalls.SelectMany(c => c.Privileges).ToList();
        Expect.Equal(2, added.Count, "REQ-11: Delete on tn_complaint (security file) and prvBulkDelete (role file) should both be added.");
        Expect.True(added.Any(a => a.Privilege.TableRight == TablePrivilege.Delete && a.Privilege.TableLogicalName == Complaint), "REQ-11: the table security file's Delete should be added.");
        Expect.True(added.Any(a => a.Privilege.PrivilegeName == "prvBulkDelete"), "REQ-11: the role file's prvBulkDelete should be added.");
    }

    // REQ-11: it asks the one question solution import always asks, once for the whole run.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task SolutionImport_AsksOnceForTheWholeRun()
    {
        using var console = new ConsoleHarness("y");
        var client = ImportEnvironment();

        var exitCode = await Run(console, client);

        Expect.Equal(0, exitCode, $"REQ-11: confirming once should import everything. Output: <{console.Output}>");
        Expect.Contains("Import 2 changes?", console.Output, "REQ-11: the security file and the role file are the two changes confirmed together.");
        Expect.Equal(2, client.AddRolePrivilegesCalls.Count, "REQ-11: both files should be applied after the single confirmation.");
    }

    // REQ-11: "`--table` skips role files." The named table's security file is still imported.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task SolutionImport_LimitedToATable_ImportsItsSecurityFileButSkipsRoleFiles()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();

        var exitCode = await Run(console, client, "--table", "tn_complaint", "--yes");

        Expect.Equal(0, exitCode, $"REQ-11: --table should succeed. Output: <{console.Output}>");
        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal(Complaint, added.Privilege.TableLogicalName ?? "", "REQ-11: with --table the table's security file should be imported...");
        Expect.True(added.Privilege.PrivilegeName != "prvBulkDelete", "REQ-11: ...and the role file should be skipped.");
    }
}

using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Commands.Security;

/// <summary>
/// Issue #21 (security role privileges as YAML). <c>security import --input path [--table t]... [--role r]... [--allow-removals] [--yes] [--whatif]</c>
/// through the real command tree (<see cref="SecurityCliHost"/>), the real <see cref="D365Architect.Services.Conversion.SecurityImportService"/>
/// and a fake Dataverse (<see cref="SecurityFixtures.ImportEnvironment"/>, where HIS User holds everything on tn_complaint except Delete,
/// Assign and Share, plus prvExportToExcel and Read on account). Input is a solution folder (<c>tn_complaint/tn_complaint.security.yml</c>,
/// <c>roles/his-user.role.yml</c>) or a single file. The files are written as raw YAML in the frozen format, not through the serializers.
/// The question asked is the one <c>solution import</c> asks ("Import N changes?", N being changed files). A prompt nobody answered fails the test
/// (that is how "--yes" and "--whatif" are shown never to prompt). In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "security-roles-yaml")]
public sealed class ImportSecurityCommandTests : IDisposable
{
    private readonly TempDirectory _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_folder.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string TableFile(string content) => Write("tn_complaint/tn_complaint.security.yml", content);

    private string RoleFile(string content) => Write("roles/his-user.role.yml", content);

    private Task<int> Run(ConsoleHarness console, FakeDataverseClient client, string input, params string[] extraArgs) =>
        new SecurityCliHost { Client = client }.RunAsync(console, ["security", "import", "--input", input, .. extraArgs]);

    private static string AllOutput(ConsoleHarness console) => $"Output: <{console.Output}>";

    // REQ-7: "`security import --input` takes a solution folder or a single file... shows a diff against live role privileges and asks before writing."
    // Example: Change HIS User Delete None -> User: diff `Delete: None -> User`, prompt "Import 1 change?".
    // Code path: ImportSecurityCommand (security import) -> SecurityImportService.PreviewTableSecurityAsync -> prompt -> ApplyAsync.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task SecurityImport_ShowsTheDiffAsksOnceAndAppliesWhenConfirmed()
    {
        using var console = new ConsoleHarness("y");
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));

        var exitCode = await Run(console, client, _folder.Path);

        Expect.Equal(0, exitCode, $"REQ-7: a confirmed import should succeed. {AllOutput(console)}");
        Expect.Contains("Delete: None → User", console.Output, "REQ-7: the diff should show Delete going from None to User.");
        Expect.Contains("Import 1 change?", console.Output, "REQ-7: the prompt should ask about the one changed file.");
        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Delete, added.Privilege.TableRight, "REQ-8: confirming should add the Delete privilege to HIS User.");
    }

    // REQ-7: "asks before writing": saying no writes nothing.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task SecurityImport_WhenDeclined_WritesNothing()
    {
        using var console = new ConsoleHarness("n");
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));

        var exitCode = await Run(console, client, _folder.Path);

        Expect.Equal(0, exitCode, $"REQ-7: declining is not an error. {AllOutput(console)}");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-7: nothing should be written when the prompt is declined.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-7: nothing should be removed when the prompt is declined.");
    }

    // REQ-7: one confirmation covers the whole folder: two changed files, "Import 2 changes?", one answer.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task SecurityImport_AFolderWithTwoChangedFilesAsksOnceForBoth()
    {
        using var console = new ConsoleHarness("y");
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));
        RoleFile(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var exitCode = await Run(console, client, _folder.Path, "--solution", SolutionName);

        Expect.Equal(0, exitCode, $"REQ-7: the import should succeed. {AllOutput(console)}");
        Expect.Contains("Import 2 changes?", console.Output, "REQ-7: the prompt should cover both changed files at once.");
        Expect.Equal(2, client.AddRolePrivilegesCalls.Count, "REQ-8/REQ-18: each file's change should be written (one add per role per file).");
    }

    // REQ-7: "takes a solution folder or a single file". A single table security file.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task SecurityImport_ASingleTableSecurityFile_IsImported()
    {
        using var console = new ConsoleHarness("y");
        var client = ImportEnvironment();
        var file = TableFile(ComplaintFileYaml(delete: "User"));

        var exitCode = await Run(console, client, file);

        Expect.Equal(0, exitCode, $"REQ-7: importing a single table security file should succeed. {AllOutput(console)}");
        Expect.Equal(1, client.AddRolePrivilegesCalls.Count, "REQ-8: the file's change should be written.");
    }

    // REQ-7/REQ-18: ...or a single role file.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task SecurityImport_ASingleRoleFile_IsImported()
    {
        using var console = new ConsoleHarness("y");
        var client = ImportEnvironment();
        var file = RoleFile(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var exitCode = await Run(console, client, file, "--solution", SolutionName);

        Expect.Equal(0, exitCode, $"REQ-18: importing a single role file should succeed. {AllOutput(console)}");
        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal("prvBulkDelete", added.Privilege.PrivilegeName ?? "", "REQ-18: the new non-table privilege should be added.");
    }

    // REQ-12: "`--whatif` on `security import` shows the diff, never prompts, never writes." (An unanswered prompt would fail the test.)
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task SecurityImport_WhatIf_ShowsTheDiffNeverPromptsAndNeverWrites()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));

        var exitCode = await Run(console, client, _folder.Path, "--whatif");

        Expect.Equal(0, exitCode, $"REQ-12: --whatif should succeed. {AllOutput(console)}");
        Expect.Contains("Delete: None → User", console.Output, "REQ-12: --whatif should still show the diff.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-12: --whatif should write nothing.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-12: --whatif should remove nothing.");
    }

    // "--yes ... behave as for other files": no prompt, and it writes.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task SecurityImport_Yes_AppliesWithoutPrompting()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));

        var exitCode = await Run(console, client, _folder.Path, "--yes");

        Expect.Equal(0, exitCode, $"REQ-7: --yes should import without asking. {AllOutput(console)}");
        Expect.Equal(1, client.AddRolePrivilegesCalls.Count, "REQ-8: --yes should still write the change.");
    }

    // A file that matches live has nothing to import: no prompt, no writes.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task SecurityImport_NothingChanged_DoesNotPromptOrWrite()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml());

        var exitCode = await Run(console, client, _folder.Path);

        Expect.Equal(0, exitCode, $"REQ-7: an unchanged file is not an error. {AllOutput(console)}");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-7: nothing differs, so nothing should be written.");
    }

    // REQ-6: "optional repeatable `--table` limits to those tables' ..." for import (REQ-7), and REQ-19: "`--table` alone skips role files."
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task SecurityImport_LimitedToATable_ImportsOnlyThatTablesFileAndSkipsRoleFiles()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));
        RoleFile(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var exitCode = await Run(console, client, _folder.Path, "--table", "tn_complaint", "--yes");

        Expect.Equal(0, exitCode, $"REQ-7: --table should succeed. {AllOutput(console)}");
        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Delete, added.Privilege.TableRight, "REQ-19: with --table only the table file's change should be written; the role file (prvBulkDelete) should be skipped.");
    }

    // REQ-19: "Optional repeatable `--role` limits security export/import to those role files."
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public async Task SecurityImport_LimitedToARole_ImportsOnlyThatRolesFileAndSkipsTableFiles()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));
        RoleFile(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var exitCode = await Run(console, client, _folder.Path, "--solution", SolutionName, "--role", "HIS User", "--yes");

        Expect.Equal(0, exitCode, $"REQ-19: --role should succeed. {AllOutput(console)}");
        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal("prvBulkDelete", added.Privilege.PrivilegeName ?? "", "REQ-19: with --role only the role file's change should be written; the table file's Delete should be skipped.");
    }

    // REQ-20 (import side): a --table with no folder under the input fails with a clear error, as `solution import --table` does.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task SecurityImport_ATableWithNoFolder_FailsNamingItAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));

        var exitCode = await Run(console, client, _folder.Path, "--table", "tn_typo", "--yes");

        Expect.True(exitCode != 0, $"REQ-20: a table with no folder should be an error exit. {AllOutput(console)}");
        Expect.Contains("tn_typo", console.Output, "REQ-20: the error should name the table.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-20: nothing should be written when the request is invalid.");
    }

    // REQ-9: "A privilege held live but missing / None in the file is only removed with `--allow-removals`; otherwise kept and reported."
    // Example: Live Share=User, file None -> without flag "~ would remove Share (kept)". Nothing else changes, so nothing is asked or written.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task SecurityImport_ALivePrivilegeTheFileSaysNone_WithoutTheFlag_IsKeptAndReported()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic));
        TableFile(ComplaintFileYaml(share: "None"));

        var exitCode = await Run(console, client, _folder.Path);

        Expect.Equal(0, exitCode, $"REQ-9: keeping a privilege is not an error. {AllOutput(console)}");
        Expect.Contains("would remove", console.Output, "REQ-9: the report should say it would remove the privilege.");
        Expect.Contains("Share", console.Output, "REQ-9: the report should name the privilege.");
        Expect.Contains("(kept)", console.Output, "REQ-9: the report should say the privilege was kept.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-9: without --allow-removals nothing should be removed.");
    }

    // REQ-9: "...with the flag, removed."
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task SecurityImport_ALivePrivilegeTheFileSaysNone_WithAllowRemovals_IsRemoved()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic));
        TableFile(ComplaintFileYaml(share: "None"));

        var exitCode = await Run(console, client, _folder.Path, "--allow-removals", "--yes");

        Expect.Equal(0, exitCode, $"REQ-9: --allow-removals should succeed. {AllOutput(console)}");
        var removed = Assert.Single(Assert.Single(client.RemoveRolePrivilegesCalls).Privileges);
        Expect.Equal<TablePrivilege?>(TablePrivilege.Share, removed.TableRight, "REQ-9: with --allow-removals Share should be removed.");
    }

    // REQ-10: "A role named in the file that doesn't exist live fails that file's import with a clear message; roles are never created."
    // Example: "Role 'HIS Auditor' not found".
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task SecurityImport_ARoleThatDoesNotExist_FailsWithAClearMessageAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        var file = TableFile(ComplaintFileYaml(role: "HIS Auditor", delete: "User"));

        var exitCode = await Run(console, client, file, "--yes");

        Expect.True(exitCode != 0, $"REQ-10: a missing role should be an error exit. {AllOutput(console)}");
        Expect.Contains("Role 'HIS Auditor' not found", console.Output, "REQ-10: the message should say which role was not found.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-10: roles are never created and nothing should be written for the failed file.");
    }

    // REQ-10: it is "that file's import" that fails: another file in the same folder is still imported, and the exit code says something failed.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task SecurityImport_AFileWithAMissingRoleDoesNotStopTheOtherFiles()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(role: "HIS Auditor", delete: "User"));
        RoleFile(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var exitCode = await Run(console, client, _folder.Path, "--solution", SolutionName, "--yes");

        Expect.True(exitCode != 0, $"REQ-10: the failed file should make the run exit non-zero. {AllOutput(console)}");
        Expect.Contains("Role 'HIS Auditor' not found", console.Output, "REQ-10: the failure should be reported.");
        var added = Assert.Single(Assert.Single(client.AddRolePrivilegesCalls).Privileges);
        Expect.Equal("prvBulkDelete", added.Privilege.PrivilegeName ?? "", "REQ-10: the role file, which is fine, should still be imported.");
    }

    // REQ-18: "Add `prvBulkDelete: User` -> `+ prvBulkDelete: User`; delete a line without the flag -> 'would remove (kept)'."
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task SecurityImport_ARoleFileThatAddsAPrivilege_ShowsItAsAnAddition()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        var file = RoleFile(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var exitCode = await Run(console, client, file, "--solution", SolutionName, "--whatif");

        Expect.Equal(0, exitCode, $"REQ-18: --whatif should succeed. {AllOutput(console)}");
        Expect.Contains("+ prvBulkDelete: User", console.Output, "REQ-18: an added non-table privilege should be shown as '+ prvBulkDelete: User'.");
    }

    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task SecurityImport_ARoleFileThatDropsAPrivilege_WithoutTheFlag_ReportsItAsWouldRemoveKept()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        var file = RoleFile(HisUserRoleYaml(privileges: []));

        var exitCode = await Run(console, client, file, "--solution", SolutionName);

        Expect.Equal(0, exitCode, $"REQ-18: keeping a privilege is not an error. {AllOutput(console)}");
        Expect.Contains("would remove", console.Output, "REQ-18: the report should say it would remove the privilege.");
        Expect.Contains("prvExportToExcel", console.Output, "REQ-18: the report should name the privilege dropped from the file.");
        Expect.Contains("(kept)", console.Output, "REQ-18: the report should say the privilege was kept.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-18: without --allow-removals nothing should be removed.");
    }

    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task SecurityImport_ARoleFileThatDropsAPrivilege_WithAllowRemovals_RemovesIt()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        var file = RoleFile(HisUserRoleYaml(privileges: []));

        var exitCode = await Run(console, client, file, "--solution", SolutionName, "--allow-removals", "--yes");

        Expect.Equal(0, exitCode, $"REQ-18: --allow-removals should succeed. {AllOutput(console)}");
        var removed = Assert.Single(Assert.Single(client.RemoveRolePrivilegesCalls).Privileges);
        Expect.Equal("prvExportToExcel", removed.PrivilegeName ?? "", "REQ-18: with --allow-removals the dropped privilege should be removed.");
    }

    // REQ-18: a role file is the full picture of the role's privileges outside the solution. The `account:` entry has been deleted from `tables:`.
    // Without the flag it is reported as "would remove ... (kept)", nothing is asked and nothing is written.
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task SecurityImport_ARoleFileWithAnOutsideTableEntryDeleted_WithoutTheFlag_ReportsItAsWouldRemoveKept()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        var file = RoleFile(HisUserRoleYaml(accountKeys: []));

        var exitCode = await Run(console, client, file, "--solution", SolutionName);

        Expect.Equal(0, exitCode, $"REQ-18: keeping a privilege is not an error. {AllOutput(console)}");
        Expect.Contains("would remove", console.Output, "REQ-18: the report should say it would remove the privilege.");
        Expect.Contains("account", console.Output, "REQ-18: the report should name the table whose entry was deleted.");
        Expect.Contains("(kept)", console.Output, "REQ-18: the report should say the privilege was kept.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-18: without --allow-removals nothing should be removed.");
    }

    // REQ-18: "...and only removed with --allow-removals."
    [Fact]
    [Trait("Requirement", "REQ-18")]
    public async Task SecurityImport_ARoleFileWithAnOutsideTableEntryDeleted_WithAllowRemovals_RemovesThatTablesPrivileges()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        var file = RoleFile(HisUserRoleYaml(accountKeys: []));

        var exitCode = await Run(console, client, file, "--solution", SolutionName, "--allow-removals", "--yes");

        Expect.Equal(0, exitCode, $"REQ-18: --allow-removals should succeed. {AllOutput(console)}");
        var removed = Assert.Single(Assert.Single(client.RemoveRolePrivilegesCalls).Privileges);
        Expect.Equal("account", removed.TableLogicalName ?? "", "REQ-18: with --allow-removals the deleted table's privilege should be removed.");
        Expect.Equal<TablePrivilege?>(TablePrivilege.Read, removed.TableRight, "REQ-18: it is the Read on account.");
    }

    // REQ-17/18: a role file never removes a privilege on one of the solution's own tables, even with --allow-removals (tn_complaint is not in the file).
    [Fact]
    [Trait("Requirement", "REQ-17")]
    public async Task SecurityImport_ARoleFile_NeverRemovesPrivilegesOnATableInTheSolutionEvenWithAllowRemovals()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        var file = RoleFile(HisUserRoleYaml());

        var exitCode = await Run(console, client, file, "--solution", SolutionName, "--allow-removals", "--yes");

        Expect.Equal(0, exitCode, $"REQ-17: the role file matches everything it owns. {AllOutput(console)}");
        Expect.True(!console.Output.Contains(Complaint, StringComparison.Ordinal), $"REQ-17: tn_complaint is a table of the solution, so a role file should never report it. {AllOutput(console)}");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-17: HIS User's tn_complaint privileges belong to tn_complaint's security file and must not be removed by the role file.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-17: nothing the role file owns differs, so nothing should be added.");
    }

    // REQ-24: "`security import` with a `--role "X"` where no role named X is in the solution fails with a clear error containing the role name and
    // solution name, non-zero exit, nothing written or imported." Mirrors REQ-20. The role files in the folder are valid and would otherwise be imported.
    [Fact]
    [Trait("Requirement", "REQ-24")]
    public async Task SecurityImport_ARoleNotInTheSolution_FailsNamingTheRoleAndSolutionAndImportsNothing()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        TableFile(ComplaintFileYaml(delete: "User"));
        RoleFile(HisUserRoleYaml(privileges: ["prvExportToExcel: Organisation", "prvBulkDelete: User"]));

        var exitCode = await Run(console, client, _folder.Path, "--solution", SolutionName, "--role", "HIS Auditor", "--yes");

        Expect.True(exitCode != 0, $"REQ-24: a role that is not in the solution should be an error exit. {AllOutput(console)}");
        Expect.Contains("HIS Auditor", console.Output, "REQ-24: the error should name the role.");
        Expect.Contains(SolutionName, console.Output, "REQ-24: the error should name the solution.");
        Expect.Empty(client.AddRolePrivilegesCalls, "REQ-24: nothing should be imported when the request is invalid.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "REQ-24: nothing should be removed when the request is invalid.");
    }
}

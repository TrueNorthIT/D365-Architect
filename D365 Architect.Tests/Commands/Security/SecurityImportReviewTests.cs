using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Commands.Security;

/// <summary>
/// Issue #21 review follow-up, through the real command tree (<see cref="SecurityCliHost"/>): shared privileges (one id behind several
/// tables) shown and checked across every file of a run, a security file whose <c>table:</c> isn't its folder, a kept privilege under
/// <c>solution import</c>, and the <c>--allow-removals</c> description. tn_activity and tn_task share one Create privilege here.
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityImportReviewTests : IDisposable
{
    private static readonly Guid CreateActivity = Guid.Parse("c0000000-0000-0000-0000-0000000000ac");

    private readonly TempDirectory _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_folder.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string TableYaml(string table, string create) =>
        $"table: {table}\nroles:\n  - name: HIS User\n    create: {create}\n    read: None\n    write: None\n    delete: None\n    append: None\n    appendTo: None\n    assign: None\n    share: None\n";

    /// <summary>The import environment plus tn_activity and tn_task in the solution, sharing one Create privilege (with email outside it).</summary>
    private static FakeDataverseClient SharedEnvironment()
    {
        var client = ImportEnvironment();
        client.SolutionEntityLogicalNames = _ => [Complaint, LookupCode, "tn_activity", "tn_task"];
        client.SharedPrivileges.Add((CreateActivity, "prvCreateActivity",
            [new TablePrivilegeRef("tn_activity", TablePrivilege.Create), new TablePrivilegeRef("tn_task", TablePrivilege.Create), new TablePrivilegeRef("email", TablePrivilege.Create)]));
        return client;
    }

    private Task<int> SecurityImport(ConsoleHarness console, FakeDataverseClient client, string input, params string[] extraArgs) =>
        new SecurityCliHost { Client = client }.RunAsync(console, ["security", "import", "--input", input, .. extraArgs]);

    private Task<int> SolutionImport(ConsoleHarness console, FakeDataverseClient client, params string[] extraArgs) =>
        new SecurityCliHost { Client = client }.RunAsync(console, ["solution", "import", "--input", _folder.Path, "--solution", SolutionName, .. extraArgs]);

    private static string All(ConsoleHarness console) => $"Output: <{console.Output}>";

    // A: the preview of a change to a shared privilege names every other table it changes.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task SecurityImport_ASharedPrivilege_PreviewNamesEveryOtherTableItChanges()
    {
        using var console = new ConsoleHarness();
        var file = Write("tn_activity/tn_activity.security.yml", TableYaml("tn_activity", "User"));

        var exitCode = await SecurityImport(console, SharedEnvironment(), file, "--whatif");

        Expect.Equal(0, exitCode, All(console));
        Expect.Contains("Create: None → User (also changes: email, tn_task)", console.Output, "the plan should name every other table the shared privilege changes.");
    }

    // A: two files of one folder disagreeing on a shared privilege: both fail, nothing is written for it.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task SecurityImport_TwoFilesDisagreeingOnASharedPrivilege_FailBothAndWriteNothing()
    {
        using var console = new ConsoleHarness();
        var client = SharedEnvironment();
        Write("tn_activity/tn_activity.security.yml", TableYaml("tn_activity", "User"));
        Write("tn_task/tn_task.security.yml", TableYaml("tn_task", "Organisation"));

        var exitCode = await SecurityImport(console, client, _folder.Path, "--yes");

        Expect.True(exitCode != 0, $"a conflict should be an error exit. {All(console)}");
        Expect.Contains("conflict", console.Output.ToLowerInvariant(), "the conflict should be reported.");
        Expect.Contains("prvCreateActivity", console.Output, "the report should name the privilege.");
        Expect.Contains("tn_activity.security.yml", console.Output, "the report should name each conflicting file.");
        Expect.Contains("tn_task.security.yml", console.Output, "the report should name each conflicting file.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing should be written for a conflicting privilege (and its files fail as a whole).");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "nothing should be removed.");
    }

    // A: two files agreeing on a shared privilege: one write.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task SecurityImport_TwoFilesAgreeingOnASharedPrivilege_WriteItOnce()
    {
        using var console = new ConsoleHarness();
        var client = SharedEnvironment();
        Write("tn_activity/tn_activity.security.yml", TableYaml("tn_activity", "User"));
        Write("tn_task/tn_task.security.yml", TableYaml("tn_task", "User"));

        var exitCode = await SecurityImport(console, client, _folder.Path, "--yes");

        Expect.Equal(0, exitCode, All(console));
        Expect.Equal(1, client.AddRolePrivilegesCalls.SelectMany(c => c.Privileges).Count(), "the shared privilege should be written once, not once per file.");
    }

    // A: the cross-file check also runs across solution import's security files.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task SolutionImport_TwoFilesDisagreeingOnASharedPrivilege_FailBothAndWriteNothing()
    {
        using var console = new ConsoleHarness();
        var client = SharedEnvironment();
        Write("tn_activity/tn_activity.security.yml", TableYaml("tn_activity", "User"));
        Write("tn_task/tn_task.security.yml", TableYaml("tn_task", "Organisation"));

        var exitCode = await SolutionImport(console, client, "--yes");

        Expect.True(exitCode != 0, $"a conflict should be an error exit. {All(console)}");
        Expect.Contains("prvCreateActivity", console.Output, "the conflict should name the privilege.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing should be written for a conflicting privilege.");
    }

    // A: a role file and a table file of one solution import disagreeing on a shared privilege is a conflict too.
    [Fact]
    [Trait("Requirement", "REVIEW-A")]
    public async Task SolutionImport_ARoleFileAndATableFileDisagreeingOnASharedPrivilege_Conflict()
    {
        using var console = new ConsoleHarness();
        var client = SharedEnvironment();
        client.RolePrivileges[HisUserId] = [.. client.RolePrivileges[HisUserId],
            OnTable("tn_activity", TablePrivilege.Create, PrivilegeDepth.Basic), OnTable("tn_task", TablePrivilege.Create, PrivilegeDepth.Basic), OnTable("email", TablePrivilege.Create, PrivilegeDepth.Basic)];
        Write("tn_activity/tn_activity.security.yml", TableYaml("tn_activity", "Organisation"));
        Write("roles/his-user.role.yml", "name: HIS User\nprivileges:\n  prvExportToExcel: Organisation\ntables:\n  account:\n    read: Organisation\n  email:\n    create: User\n");

        var exitCode = await SolutionImport(console, client, "--yes");

        Expect.True(exitCode != 0, $"the role file still says User for the privilege the table file raises: a conflict. {All(console)}");
        Expect.Contains("prvCreateActivity", console.Output, "the conflict should name the privilege.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing should be written for a conflicting privilege.");
    }

    // 2: a security file whose table: isn't its folder's name is an error for that file.
    [Fact]
    [Trait("Requirement", "REVIEW-2")]
    public async Task SecurityImport_ASecurityFileWhoseTableIsNotItsFolder_FailsThatFile()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        Write("tn_lookupcode/tn_lookupcode.security.yml", ComplaintFileYaml(delete: "User"));

        var exitCode = await SecurityImport(console, client, _folder.Path, "--yes");

        Expect.True(exitCode != 0, $"a mismatched table should be an error exit. {All(console)}");
        Expect.Contains("tn_lookupcode", console.Output, "the error should name the folder.");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing should be written from that file.");
    }

    [Fact]
    [Trait("Requirement", "REVIEW-2")]
    public async Task SolutionImport_ASecurityFileWhoseTableIsNotItsFolder_FailsThatFile()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment();
        Write("tn_lookupcode/tn_lookupcode.security.yml", ComplaintFileYaml(delete: "User"));

        var exitCode = await SolutionImport(console, client, "--yes");

        Expect.True(exitCode != 0, $"a mismatched table should be an error exit. {All(console)}");
        Expect.Empty(client.AddRolePrivilegesCalls, "nothing should be written from that file.");
    }

    // 7: under solution import, a privilege held live but missing from the file is kept without --allow-removals.
    [Fact]
    [Trait("Requirement", "REVIEW-7")]
    public async Task SolutionImport_ALivePrivilegeTheFileSaysNone_WithoutAllowRemovals_IsKept()
    {
        using var console = new ConsoleHarness();
        var client = ImportEnvironment(extraHisUserPrivileges: OnTable(Complaint, TablePrivilege.Share, PrivilegeDepth.Basic));
        Write("tn_complaint/tn_complaint.security.yml", ComplaintFileYaml(share: "None"));

        var exitCode = await SolutionImport(console, client, "--yes");

        Expect.Equal(0, exitCode, All(console));
        Expect.Contains("(kept)", console.Output, "the privilege should be reported as kept.");
        Expect.Empty(client.RemoveRolePrivilegesCalls, "without --allow-removals nothing should be removed.");
    }

    // B: solution import's single --allow-removals says it also removes role privileges.
    [Fact]
    [Trait("Requirement", "REVIEW-B")]
    public async Task SolutionImport_AllowRemovalsHelp_SaysItAlsoRemovesRolePrivileges()
    {
        using var console = new ConsoleHarness();

        var exitCode = await new SecurityCliHost().RunAsync(console, "solution", "import", "--help");

        Expect.Equal(0, exitCode, All(console));
        var line = string.Join(" ", console.Output.Split('\n').SkipWhile(l => !l.Contains("--allow-removals", StringComparison.Ordinal)).Take(4));
        Expect.Contains("privilege", line, "--allow-removals' description should say it also removes role privileges.");
    }
}

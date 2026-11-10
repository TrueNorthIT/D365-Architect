using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.SecurityFixtures;

namespace D365Architect.Tests.Commands.Security;

/// <summary>
/// Issue #21 (security role privileges as YAML). What <c>solution export</c> writes to disk for security, through the real command tree
/// (<see cref="SecurityCliHost"/>) with the export service itself faked (it returns a canned <see cref="SolutionExportResult"/>; how
/// that is built is covered by <c>SolutionExportServiceSecurityTests</c>). The files the command writes are the ones in the requirements:
/// a security file in each table's folder, and one file per role under <c>roles/</c> at the solution root.
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "security-roles-yaml")]
public sealed class SolutionExportCommandSecurityTests : IDisposable
{
    private const string ComplaintSecurity = "table: tn_complaint\nroles:\n  - name: HIS User\n";
    private const string HisUserRole = "name: HIS User\nprivileges:\n  prvExportToExcel: Organisation\n";

    private sealed class CannedSolutionExport(SolutionExportResult result) : ISolutionExportService
    {
        public Task<SolutionExportResult> ExportSolutionAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken, IReadOnlyCollection<string>? tables = null) =>
            Task.FromResult(result);
    }

    private readonly TempDirectory _output = new();

    public void Dispose() => _output.Dispose();

    private static SolutionExportResult Result() => new(
        [
            new SolutionExportEntity(Complaint, "logicalName: tn_complaint\n", [], [], SecurityYaml: ComplaintSecurity),
            new SolutionExportEntity(LookupCode, "logicalName: tn_lookupcode\n", [], [], SecurityYaml: null),
        ],
        ChoicesYaml: null,
        Roles: [new ExportedRole("his-user", "HIS User", HisUserRole), new ExportedRole("his-super-user", "HIS Super User", "name: HIS Super User\n")]);

    private Task<int> Run(ConsoleHarness console) =>
        new SecurityCliHost { SolutionExport = new CannedSolutionExport(Result()) }.RunAsync(console, "solution", "export", "--solution", SolutionName, "--output", _output.Path);

    // REQ-1: "`solution export` writes a security file into each table's folder." Example: tn_complaint/ gets tn_complaint.security.yml.
    // Code path: ExportSolutionCommand writes SolutionExportEntity.SecurityYaml to <output>/<solution>/<table>/<table>.security.yml.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task SolutionExport_WritesATablesSecurityFileIntoItsFolder()
    {
        using var console = new ConsoleHarness();

        var exitCode = await Run(console);


        Expect.Equal(0, exitCode, $"REQ-1: solution export should succeed. Output: <{console.Output}>");
        var path = Path.Combine(_output.Path, SolutionName, Complaint, $"{Complaint}.security.yml");
        Expect.True(File.Exists(path), $"REQ-1: expected '{path}' to be written.");
        Expect.Equal(ComplaintSecurity, File.ReadAllText(path), "REQ-1: the security file's text should be what the export produced, unchanged.");
    }

    // REQ-4: "A table no solution role has access to gets no security file (never an empty one)." Its table file is still written.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task SolutionExport_ATableWithoutSecurityGetsNoSecurityFileButKeepsItsTableFile()
    {
        using var console = new ConsoleHarness();

        await Run(console);


        Expect.True(File.Exists(Path.Combine(_output.Path, SolutionName, Complaint, $"{Complaint}.security.yml")), "REQ-1: tn_complaint has security, so its security file should be written (the contrast with tn_lookupcode below).");
        Expect.True(!File.Exists(Path.Combine(_output.Path, SolutionName, LookupCode, $"{LookupCode}.security.yml")), "REQ-4: tn_lookupcode has no security, so no security file should be written for it.");
        Expect.True(File.Exists(Path.Combine(_output.Path, SolutionName, LookupCode, $"{LookupCode}.table.yml")), "the table's own file should still be written.");
    }

    // REQ-13: "`solution export` writes one file per solution role under `roles/` at the solution root." Example: roles/his-user.role.yml, roles/his-super-user.role.yml.
    [Fact]
    [Trait("Requirement", "REQ-13")]
    public async Task SolutionExport_WritesOneFilePerRoleUnderRolesAtTheSolutionRoot()
    {
        using var console = new ConsoleHarness();

        await Run(console);


        var rolesFolder = Path.Combine(_output.Path, SolutionName, "roles");
        Expect.Sequence(["his-super-user.role.yml", "his-user.role.yml"], Directory.Exists(rolesFolder) ? Directory.GetFiles(rolesFolder).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList() : [], "REQ-13: one role file per role under roles/.");
        Expect.Equal(HisUserRole, File.ReadAllText(Path.Combine(rolesFolder, "his-user.role.yml")), "REQ-13: the role file's text should be what the export produced, unchanged.");
    }
}

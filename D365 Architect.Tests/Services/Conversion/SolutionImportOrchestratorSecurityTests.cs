using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 (security role privileges as YAML), REQ-11: "`solution import` imports each table's security file after its forms, and role
/// files after all table folders; `--whatif`, `--yes`, `--table` behave as for other files; `--table` skips role files."
/// Covers the stage ordering in <see cref="SolutionImportOrchestrator"/>, with a recording <see cref="ISolutionImportStepRunner"/> as in
/// <c>SolutionImportOrchestratorTests</c>: a table security file is an <see cref="SolutionImportStage.ImportSecurity"/> step, a role file
/// (under <c>roles/</c> at the solution root) an <see cref="SolutionImportStage.ImportRole"/> step.
/// The folder is the layout <c>solution export</c> writes: choices.yml, one folder per table (tn_project and tn_task) and <c>roles/</c>.
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SolutionImportOrchestratorSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public SolutionImportOrchestratorSecurityTests()
    {
        Write("choices.yml", "- name: tn_priority\n");
        Write("tn_project/tn_project.table.yml", TableCreateFixtures.TableYaml());
        Write("tn_project/Active projects.view.yml", "name: Active projects\n");
        Write("tn_project/Main.form.yml", "name: Main\n");
        Write("tn_project/tn_project.security.yml", "table: tn_project\nroles: []\n");
        Write("tn_task/tn_task.table.yml", "entity: tn_task\n");
        Write("tn_task/tn_task.security.yml", "table: tn_task\nroles: []\n");
        // Written out of name order on purpose: the run is in file-name order, as the other stages are.
        Write("roles/his-user.role.yml", "name: HIS User\n");
        Write("roles/his-super-user.role.yml", "name: HIS Super User\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class RecordingRunner : ISolutionImportStepRunner
    {
        public List<string> Log { get; } = [];
        public List<string> Skipped { get; } = [];
        public Func<SolutionImportStage, string, bool> Fails { get; set; } = (_, _) => false;
        public Func<IReadOnlyList<string>, SolutionTableCreateResult> CreateResult { get; set; } = _ => new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, []);

        public Task<SolutionTableCreateResult> CreateTablesAsync(IReadOnlyList<string> tableAssetPaths, CancellationToken cancellationToken)
        {
            Log.Add($"CreateTables:{string.Join(",", tableAssetPaths.Select(p => Path.GetFileName(p)))}");
            return Task.FromResult(CreateResult(tableAssetPaths));
        }

        public Task<SolutionImportStepOutcome> RunAsync(SolutionImportStage stage, string assetPath, CancellationToken cancellationToken)
        {
            var fileName = Path.GetFileName(assetPath);
            Log.Add($"{stage}:{fileName}");
            return Task.FromResult(Fails(stage, fileName) ? SolutionImportStepOutcome.Failed : SolutionImportStepOutcome.Succeeded);
        }

        public void ReportSkipped(SolutionImportStage stage, string assetPath, string tableLogicalName) =>
            Skipped.Add($"{stage}:{Path.GetFileName(assetPath)} ({tableLogicalName})");
    }

    // REQ-11: "imports each table's security file after its forms, and role files after all table folders."
    // Code path: SolutionImportOrchestrator.RunAsync -> ISolutionImportStepRunner.RunAsync(ImportSecurity/ImportRole, path).
    // The roles folder is not a table folder: it never contributes to the create-tables batch or to a table's own steps.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task RunAsync_ImportsATablesSecurityFileAfterItsFormsAndRoleFilesAfterEveryTableFolder()
    {
        var runner = new RecordingRunner();

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Sequence(
            [
                "CreateTables:tn_project.table.yml,tn_task.table.yml",
                "ImportChoices:choices.yml",
                "ImportTable:tn_project.table.yml",
                "ImportView:Active projects.view.yml",
                "ImportForm:Main.form.yml",
                "ImportSecurity:tn_project.security.yml",
                "ImportTable:tn_task.table.yml",
                "ImportSecurity:tn_task.security.yml",
                "ImportRole:his-super-user.role.yml",
                "ImportRole:his-user.role.yml",
            ],
            runner.Log,
            "REQ-11: solution import should run each table folder's table, views, forms and then its security file, then every role file from roles/ after all the table folders.");
    }

    // REQ-11: "`--table` skips role files." The tables named still get their security files; the role files are not imported.
    // (In production the filter has already been checked against the table folders; roles/ is not a table.)
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task RunAsync_LimitedToATable_ImportsItsSecurityFileButSkipsRoleFiles()
    {
        var runner = new RecordingRunner();

        await new SolutionImportOrchestrator().RunAsync(_root, runner, ["tn_task"], CancellationToken.None);

        Expect.Sequence(
            [
                "CreateTables:tn_task.table.yml",
                "ImportTable:tn_task.table.yml",
                "ImportSecurity:tn_task.security.yml",
            ],
            runner.Log,
            "REQ-11: with --table only that table's folder should be imported, security file included, and no role file.");
    }

    // REQ-11: a solution folder with no roles/ folder (a solution with no roles) just has no role steps.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task RunAsync_NoRolesFolder_HasNoRoleSteps()
    {
        Directory.Delete(Path.Combine(_root, "roles"), recursive: true);
        var runner = new RecordingRunner();

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.True(runner.Log.All(l => !l.StartsWith("ImportRole:", StringComparison.Ordinal)), $"REQ-11: without a roles folder there is no role to import. Log: <{string.Join(" | ", runner.Log)}>.");
        Expect.Contains("ImportSecurity:tn_task.security.yml", string.Join(" | ", runner.Log), "REQ-11: the table security files are still imported.");
    }

    // REQ-11: a table folder that holds only a security file (the table itself already exists and is not in this folder) is still imported.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task RunAsync_AFolderWithOnlyASecurityFile_StillImportsIt()
    {
        File.Delete(Path.Combine(_root, "tn_task", "tn_task.table.yml"));
        var runner = new RecordingRunner();

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Contains("ImportSecurity:tn_task.security.yml", string.Join(" | ", runner.Log), "REQ-11: a security file is imported whether or not the folder also has a table file.");
    }

    // REQ-11: "`--whatif`... behave as for other files". Under --whatif a table that would be created is skipped with its later steps (the security file included).
    // The role files do not belong to any table folder, so they still preview.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task RunAsync_WhatIfTableWouldBeCreated_SkipsItsSecurityFileButStillPreviewsRoleFiles()
    {
        var runner = new RecordingRunner
        {
            CreateResult = paths => new SolutionTableCreateResult(SolutionImportStepOutcome.Succeeded, [paths.Single(p => p.EndsWith("tn_project.table.yml", StringComparison.Ordinal))]),
        };

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Contains("ImportSecurity:tn_project.security.yml (tn_project)", string.Join(" | ", runner.Skipped), "REQ-11: a would-be-created table has nothing live to preview its security against, so the file is skipped and reported.");
        Expect.True(!runner.Log.Contains("ImportSecurity:tn_project.security.yml"), "REQ-11: the skipped security file should not be run.");
        Expect.Contains("ImportRole:his-user.role.yml", string.Join(" | ", runner.Log), "REQ-11: role files are not tied to a table folder and should still be previewed.");
    }

    // As for other files: one failed step does not stop the rest.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task RunAsync_ASecurityFileFails_TheRemainingFilesAreStillImportedAndTheFailureIsCounted()
    {
        var runner = new RecordingRunner { Fails = (stage, file) => stage == SolutionImportStage.ImportSecurity && file == "tn_project.security.yml" };

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);

        Expect.Equal(1, result.Failed, "REQ-11: only the failed security file should count as a failure.");
        Expect.Contains("ImportSecurity:tn_task.security.yml", string.Join(" | ", runner.Log), "REQ-11: the next table's security file should still be imported.");
        Expect.Contains("ImportRole:his-user.role.yml", string.Join(" | ", runner.Log), "REQ-11: the role files should still be imported.");
    }
}

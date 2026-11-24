using System.Text.RegularExpressions;
using D365Architect.Commands;
using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands.Solution;

/// <summary>
/// <c>solution import</c> renames a view the same way <c>view import</c> does, because both go through
/// <see cref="ViewImportService"/>: a table folder holding one view file whose <c>savedQueryId</c> resolves to a live view with a
/// different name. Runs the real <see cref="SolutionImportOrchestrator"/> and <see cref="ImportSolutionCommand.StepRunner"/>,
/// with only Dataverse, sign-in and the console faked.
///
/// Also pins the guard for a folder exported again after a rename: <c>solution export</c> names view files after the view and never
/// removes the old one, so two files can carry the same <c>savedQueryId</c>. Neither is imported, so the stray copy can't rename the
/// view back.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered fails the test.
/// Tagged with the "Feature" trait "view-rename".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "view-rename")]
public sealed class ImportSolutionCommandViewRenameTests : IDisposable
{
    private const string Solution = "TNCore";
    private const string LiveName = "All projects";
    private const string NewName = "All projects (renamed)";
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportSolutionCommandViewRenameTests() => Directory.CreateDirectory(Path.Combine(_root, "tn_project"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ImportSolutionCommand.StepRunner CreateRunner(FakeDataverseClient client, bool whatIf = false)
    {
        var command = new ImportSolutionCommand(
            new TableCreateService(client),
            new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)),
            new ViewImportService(client),
            new FormImportService(client, new FormJsonDefinitionReader()),
            new GlobalChoiceImportService(client),
            new SecurityImportService(client),
            new ImportRunner(new FakeAuthenticationService()));
        return new ImportSolutionCommand.StepRunner(command, new ImportSolutionCommand.Settings { Input = "unused", Solution = Solution, Yes = true, WhatIf = whatIf });
    }

    /// <summary>The folder holds one view file that differs from the live view only by name.</summary>
    private FakeDataverseClient ClientWithRenamedViewInFolder()
    {
        File.WriteAllText(
            Path.Combine(_root, "tn_project", "All projects.view.yml"),
            ViewYamlSerializer.ToYaml(new ViewDefinition { Name = NewName, Entity = "tn_project", SavedQueryId = ViewId }));

        var client = new FakeDataverseClient();
        client.SavedQueriesById[ViewId] = new ExistingSavedQuery(ViewId, null, "<fetch/>", null, "tn_project", LiveName);
        return client;
    }

    // REQ-7: "solution import renames too (a solution folder with one renamed view -> counted as 1 changed, name written)."
    // Code path: SolutionImportOrchestrator -> ImportSolutionCommand.StepRunner (ImportView stage) -> ViewImportService.PreviewAsync/ApplyAsync.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_FolderWithOneRenamedView_CountsItAsChangedAndWritesTheName()
    {
        using var console = new ConsoleHarness(); // --yes
        var client = ClientWithRenamedViewInFolder();
        var runner = CreateRunner(client);

        var result = await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Equal(0, result.Failed, $"REQ-7: the rename should not fail. Output: <{console.Output}>");
        Expect.Equal(0, exitCode, $"REQ-7: the run should succeed. Output: <{console.Output}>");
        Expect.Contains("1 changed, 0 unchanged, 0 failed", console.Output, "REQ-7: a renamed view should be counted as one change.");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, $"REQ-7: the renamed view should be updated once. Output: <{console.Output}>");
        var call = client.UpdateSavedQueryCalls[0];
        Expect.Equal(NewName, call.Name, "REQ-7: the new name should be written.");
        Expect.Equal(Solution, call.SolutionUniqueName, "REQ-7: the renamed view should join solution import's --solution like any other change.");
    }

    // REQ-7 with REQ-2: under --whatif the plan counts the rename ("would change") and nothing is written.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_FolderWithOneRenamedViewUnderWhatIf_CountsItAndWritesNothing()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithRenamedViewInFolder();
        var runner = CreateRunner(client, whatIf: true);

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);
        await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Contains("1 would change, 0 unchanged, 0 failed", console.Output, "REQ-7: --whatif should count the rename as one change that would be made.");
        Expect.Contains($"'{LiveName}' -> '{NewName}'", console.Output, "REQ-2: the plan should show the rename as 'old' -> 'new'.");
        Expect.Empty(client.UpdateSavedQueryCalls, "REQ-7: --whatif must not write the rename.");
    }

    /// <summary>
    /// The folder as a second export after a rename leaves it: the old file still asks for the old name (a rename back), the new one
    /// already matches live. Both carry the same <c>savedQueryId</c>.
    /// </summary>
    private FakeDataverseClient ClientWithStaleCopyOfRenamedView(string staleName = LiveName)
    {
        WriteView(LiveName, staleName, ViewId);
        WriteView(NewName, NewName, ViewId);

        var client = new FakeDataverseClient();
        client.SavedQueriesById[ViewId] = new ExistingSavedQuery(ViewId, null, "<fetch/>", null, "tn_project", NewName);
        return client;
    }

    private void WriteView(string fileName, string viewName, Guid savedQueryId) =>
        File.WriteAllText(
            Path.Combine(_root, "tn_project", $"{fileName}.view.yml"),
            ViewYamlSerializer.ToYaml(new ViewDefinition { Name = viewName, Entity = "tn_project", SavedQueryId = savedQueryId }));

    // Long temp paths can wrap the error line, so compare with whitespace collapsed.
    private static string Flat(string text) => Regex.Replace(text, @"\s+", " ");

    private void ExpectBothFilesReportedAsDuplicates(string output)
    {
        var oldPath = Path.Combine(_root, "tn_project", $"{LiveName}.view.yml");
        var newPath = Path.Combine(_root, "tn_project", $"{NewName}.view.yml");
        var flat = Flat(output);
        Expect.Contains($"'{oldPath}' failed:", flat, $"the stale copy should be reported as failed. Output: <{output}>");
        Expect.Contains($"'{newPath}' failed:", flat, $"the current file should be reported as failed. Output: <{output}>");
        Expect.Contains($"{newPath}, {oldPath}", flat, $"the error should name every file sharing the id. Output: <{output}>");
        Expect.Contains("left over from before the view was renamed", flat, $"the error should say why there are two files. Output: <{output}>");
    }

    [Fact]
    public async Task WholeRun_TwoFilesWithTheSameSavedQueryId_AppliesNeitherAndFailsBoth()
    {
        using var console = new ConsoleHarness(); // --yes
        var client = ClientWithStaleCopyOfRenamedView();
        var runner = CreateRunner(client);

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Empty(client.UpdateSavedQueryCalls, $"neither file should be applied, or the stale one renames the view back. Output: <{console.Output}>");
        Expect.Empty(client.PublishEntityCalls, "nothing was applied, so nothing should be published.");
        ExpectBothFilesReportedAsDuplicates(console.Output);
        Expect.Contains("0 changed, 0 unchanged, 2 failed", console.Output, "both files should be counted as failed.");
        Expect.Equal(1, exitCode, "a run with failed files should exit non-zero.");
    }

    [Fact]
    public async Task WholeRun_TwoFilesWithTheSameSavedQueryIdUnderWhatIf_ReportsTheSameConflict()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithStaleCopyOfRenamedView();
        var runner = CreateRunner(client, whatIf: true);

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Empty(client.UpdateSavedQueryCalls, "--whatif must not write anything.");
        ExpectBothFilesReportedAsDuplicates(console.Output);
        Expect.Contains("0 changed, 0 unchanged, 2 failed", console.Output, "--whatif should count both files as failed.");
        Expect.Equal(1, exitCode, "--whatif should exit non-zero when a file fails.");
    }

    [Fact]
    public async Task WholeRun_TwoFilesWithTheSameSavedQueryIdAndName_AppliesNeither()
    {
        using var console = new ConsoleHarness(); // --yes
        var client = ClientWithStaleCopyOfRenamedView(staleName: NewName);
        var runner = CreateRunner(client);

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Empty(client.UpdateSavedQueryCalls, $"one of the two files is a stray copy even when the names match. Output: <{console.Output}>");
        ExpectBothFilesReportedAsDuplicates(console.Output);
        Expect.Contains("0 changed, 0 unchanged, 2 failed", console.Output, "both files should be counted as failed.");
        Expect.Equal(1, exitCode, "a run with failed files should exit non-zero.");
    }

    [Fact]
    public async Task WholeRun_TwoFilesWithTheSameSavedQueryId_StillAppliesAnUnrelatedView()
    {
        using var console = new ConsoleHarness(); // --yes
        var client = ClientWithStaleCopyOfRenamedView();
        var otherId = new Guid("2b0c3c0e-6f1a-4c0e-9a51-6d3f0c7a1e22");
        WriteView("Active projects", "Active projects (renamed)", otherId);
        client.SavedQueriesById[otherId] = new ExistingSavedQuery(otherId, null, "<fetch/>", null, "tn_project", "Active projects");

        var runner = CreateRunner(client);

        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);
        var exitCode = await runner.ConfirmAndApplyAsync(CancellationToken.None);

        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, $"only the unrelated view should be applied. Output: <{console.Output}>");
        Expect.Equal(otherId, client.UpdateSavedQueryCalls[0].SavedQueryId, "the view applied should be the unrelated one.");
        Expect.Contains("1 changed, 0 unchanged, 2 failed", console.Output, "the unrelated view should count as changed alongside the two failures.");
        Expect.Equal(1, exitCode, "the run still has failed files, so it should exit non-zero.");
    }
}

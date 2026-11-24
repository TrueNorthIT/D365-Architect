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
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered fails the test.
/// </summary>
[Collection(ConsoleCollection.Name)]
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
        Expect.Contains("1 changed, 0 unchanged, 0 failed", console.Output, "REQ-7: a renamed view should be counted as one change (today it counts as unchanged).");
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
}

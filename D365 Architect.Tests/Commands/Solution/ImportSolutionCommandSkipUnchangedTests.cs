using D365Architect.Commands;
using D365Architect.Commands.Solution;
using D365Architect.Services.Caching;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Spectre.Console.Cli;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Commands.Solution;

/// <summary>
/// <c>solution import</c> skips the full preview of a view or form that can't have changed: its file matches the hash recorded by the
/// last export or import, and Dataverse's <c>versionnumber</c> matches the one recorded then. Runs the real
/// <see cref="SolutionImportOrchestrator"/> and <see cref="ImportSolutionCommand.StepRunner"/> over a folder of one or two files, with
/// Dataverse, sign-in, the console and the <see cref="IImportCache"/> faked. "Full load" is the by-id or by-name read of the whole view or
/// form that a full preview makes (<see cref="FakeDataverseClient.SavedQueryFullLoads"/>, <see cref="FakeDataverseClient.SystemFormFullLoads"/>):
/// a skipped file makes none. In the "Console" collection because it swaps <c>AnsiConsole.Console</c>; a prompt nobody answered fails the test.
/// Tagged with the "Feature" trait "skip-unchanged-assets".
/// </summary>
[Collection(ConsoleCollection.Name)]
[Trait("Feature", "skip-unchanged-assets")]
public sealed class ImportSolutionCommandSkipUnchangedTests : IDisposable
{
    private const string Solution = "TNCore";
    private const string Table = "tn_project";
    private const string ViewName = "All projects";
    private const string FormName = "Project";
    private const string ViewDescription = "Every project";
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid FormId = new("2b0c3c0e-6f1a-4c0e-9a51-6d3f0c7a1e22");
    private static readonly EntityIdentity ProjectIdentity = new(10722, "tn_projectid", "tn_name");
    private static readonly Uri Environment = FakeAuthenticationService.EnvironmentUrl;
    private static readonly Uri OtherEnvironment = new("https://other.crm.dynamics.com");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public ImportSolutionCommandSkipUnchangedTests() => Directory.CreateDirectory(Path.Combine(_root, Table));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string ViewPath => Path.Combine(_root, Table, $"{ViewName}.view.yml");
    private string FormPath => Path.Combine(_root, Table, $"{FormName}.form.yml");

    private static string ViewText(string description = ViewDescription, Guid? id = null, string name = ViewName) =>
        ViewYamlSerializer.ToYaml(new ViewDefinition { Name = name, Entity = Table, SavedQueryId = id ?? ViewId, Description = description });

    private static string FormText(string label = "Name") => FormYamlSerializer.ToYaml(FormOf(FormName, entity: Table, formId: FormId, label: label));

    private string WriteView(string text, string? path = null)
    {
        File.WriteAllText(path ?? ViewPath, text);
        return text;
    }

    private string WriteForm(string text)
    {
        File.WriteAllText(FormPath, text);
        return text;
    }

    /// <summary>A live view that matches <see cref="ViewText"/> (version 100), and a live form that matches <see cref="FormText"/> (version 200).</summary>
    private static FakeDataverseClient ClientWithLiveViewAndForm()
    {
        var client = new FakeDataverseClient();
        client.EntityIdentities[Table] = ProjectIdentity;
        client.SavedQueriesById[ViewId] = new ExistingSavedQuery(ViewId, ViewDescription, "<fetch/>", null, Table, ViewName);
        client.SavedQueryVersions[ViewId] = (Table, 100);
        client.SystemForm = LiveForm("Name");
        client.SystemFormVersions[FormId] = (Table, 200);
        return client;
    }

    private static ExistingSystemForm LiveForm(string label) =>
        new(FormId, FormXmlWriter.Write(FormOf(FormName, entity: Table, formId: FormId, label: label)), Table, FormName);

    private static ImportSolutionCommand.StepRunner CreateRunner(FakeDataverseClient client, IImportCache cache, bool yes = true, bool whatIf = false, bool noSkip = false) =>
        new(
            new ImportSolutionCommand(
                new TableCreateService(client),
                new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)),
                new ViewImportService(client),
                new FormImportService(client, new FormJsonDefinitionReader()),
                new GlobalChoiceImportService(client),
                new SecurityImportService(client),
                new ImportRunner(new FakeAuthenticationService()),
                client,
                cache),
            new ImportSolutionCommand.Settings { Input = "unused", Solution = Solution, Yes = yes, WhatIf = whatIf, NoSkip = noSkip });

    /// <summary>A whole run, as <c>solution import</c> does it: every stage previewed, then confirmed and applied. Returns the exit code.</summary>
    private async Task<int> RunAsync(FakeDataverseClient client, IImportCache cache, bool yes = true, bool whatIf = false, bool noSkip = false)
    {
        var runner = CreateRunner(client, cache, yes, whatIf, noSkip);
        await new SolutionImportOrchestrator().RunAsync(_root, runner, CancellationToken.None);
        return await runner.ConfirmAndApplyAsync(CancellationToken.None);
    }

    // ---- REQ-7: a file nobody has edited, for an asset nobody has changed, isn't previewed ----

    // REQ-7: "A view or form whose file hasn't been edited since the last export or import, and hasn't changed in Dataverse, is counted as unchanged without the full preview."
    // Given a view file matching the cache entry (version 100) and Dataverse still at version 100, when the solution is imported,
    // then it is counted unchanged and the view is never loaded in full. Code path: ImportSolutionCommand.StepRunner.RunAsync (ImportView stage).
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_UneditedViewWhoseVersionIsUnchanged_IsCountedUnchangedWithoutLoadingItInFull()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), versionNumber: 100);

        var exitCode = await RunAsync(client, cache);

        Expect.Equal(0, client.SavedQueryFullLoads, $"REQ-7: an unedited, unchanged view should not be loaded in full. Output: <{console.Output}>");
        Expect.Contains("0 changed, 1 unchanged, 0 failed", console.Output, "REQ-7: the skipped view should be counted as unchanged.");
        Expect.Equal(0, exitCode, "REQ-7: an unchanged view is not an error.");
    }

    // As above for a form, which is versioned through systemforms. Code path: StepRunner.RunAsync (ImportForm stage).
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_UneditedFormWhoseVersionIsUnchanged_IsCountedUnchangedWithoutLoadingItInFull()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.Form, FormId, WriteForm(FormText()), versionNumber: 200);

        var exitCode = await RunAsync(client, cache);

        Expect.Equal(0, client.SystemFormFullLoads, $"REQ-7: an unedited, unchanged form should not be loaded in full. Output: <{console.Output}>");
        Expect.Contains("0 changed, 1 unchanged, 0 failed", console.Output, "REQ-7: the skipped form should be counted as unchanged.");
        Expect.Equal(0, exitCode, "REQ-7: an unchanged form is not an error.");
    }

    // REQ-7's example: "unedited solution under --whatif -> every view/form counted unchanged, and the per-file full load is never called."
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRunUnderWhatIf_UneditedViewAndForm_AreBothCountedUnchangedWithNoFullLoads()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), versionNumber: 100);
        cache.Seed(Environment, ImportAssetKind.Form, FormId, WriteForm(FormText()), versionNumber: 200);

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(0, client.SavedQueryFullLoads + client.SystemFormFullLoads, $"REQ-7: --whatif over an unedited solution should load no view or form in full. Output: <{console.Output}>");
        Expect.Contains("0 changed, 2 unchanged, 0 failed", console.Output, "REQ-7: both skipped files should be counted as unchanged in the summary.");
    }

    // REQ-7: the live versions are "one query per table per kind, not one full load per file".
    // Given two view files on tn_project, one view file on tn_task and one form file on tn_project, then each table's view versions are asked
    // for once and the form versions once, and only for the table that has forms.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_SeveralFilesOnATable_AsksForTheLiveVersionsOncePerTablePerKind()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var secondViewId = new Guid("4f3b8b7e-5a52-4d0b-8f4e-0f6f1f9d2a10");
        var taskViewId = new Guid("8c1d6a52-0e0b-4f4f-bb3e-2f5b9a7c3d11");
        client.SavedQueriesById[secondViewId] = new ExistingSavedQuery(secondViewId, ViewDescription, "<fetch/>", null, Table, "Active projects");
        client.SavedQueriesById[taskViewId] = new ExistingSavedQuery(taskViewId, ViewDescription, "<fetch/>", null, "tn_task", "All tasks");
        client.SavedQueryVersions[secondViewId] = (Table, 101);
        client.SavedQueryVersions[taskViewId] = ("tn_task", 102);
        Directory.CreateDirectory(Path.Combine(_root, "tn_task"));

        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), 100);
        var secondText = WriteView(ViewText(id: secondViewId, name: "Active projects"), Path.Combine(_root, Table, "Active projects.view.yml"));
        cache.Seed(Environment, ImportAssetKind.View, secondViewId, secondText, 101);
        var taskText = ViewYamlSerializer.ToYaml(new ViewDefinition { Name = "All tasks", Entity = "tn_task", SavedQueryId = taskViewId, Description = ViewDescription });
        WriteView(taskText, Path.Combine(_root, "tn_task", "All tasks.view.yml"));
        cache.Seed(Environment, ImportAssetKind.View, taskViewId, taskText, 102);
        cache.Seed(Environment, ImportAssetKind.Form, FormId, WriteForm(FormText()), 200);

        await RunAsync(client, cache, whatIf: true);

        Expect.Sequence([Table, "tn_task"], client.SavedQueryVersionQueries.Order().ToList(), $"REQ-7: view versions should be read once for each table, not once per view file. Output: <{console.Output}>");
        Expect.Sequence([Table], client.SystemFormVersionQueries, "REQ-7: form versions should be read once, for the one table with a form file.");
        Expect.Contains("0 changed, 4 unchanged, 0 failed", console.Output, "REQ-7: all four skipped files should be counted as unchanged.");
    }

    // REQ-7: a CRLF checkout and an LF checkout of the same file both match what was recorded.
    // Given the cache holds the hash of the LF text and the file on disk has CRLF line endings, then the view is still skipped.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_UneditedViewCheckedOutWithCrlfLineEndings_IsStillSkipped()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        var lf = ViewText().Replace("\r\n", "\n");
        cache.Seed(Environment, ImportAssetKind.View, ViewId, lf, versionNumber: 100);
        WriteView(lf.Replace("\n", "\r\n"));

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(0, client.SavedQueryFullLoads, $"REQ-7: line endings alone should not make an unedited file look edited. Output: <{console.Output}>");
        Expect.Contains("0 changed, 1 unchanged, 0 failed", console.Output, "REQ-7: the CRLF copy should still be counted as unchanged.");
    }

    // REQ-7 with the summary: "skipped files count as unchanged in the summary line".
    // Given an unedited view (skipped) beside a form whose file was edited, then the run reports 1 changed, 1 unchanged.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_SkippedViewBesideAnEditedForm_CountsOneChangedAndOneUnchanged()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), 100);
        cache.Seed(Environment, ImportAssetKind.Form, FormId, FormText(), 200);
        WriteForm(FormText(label: "Name (edited)"));

        var exitCode = await RunAsync(client, cache);

        Expect.Equal(0, client.SavedQueryFullLoads, $"REQ-7: the unedited view should be skipped, not previewed in full. Output: <{console.Output}>");
        Expect.Contains("1 changed, 1 unchanged, 0 failed", console.Output, "REQ-7: the skipped view should be counted unchanged next to the form that was imported.");
        Expect.Equal(1, client.UpdateSystemFormXmlCalls.Count, "REQ-8: the edited form should still be written.");
        Expect.Equal(0, exitCode, "the run should succeed.");
    }

    // REQ-7 (the other half of "last export or import"): a file whose full preview finds it unchanged is recorded, so the run after skips it.
    // Given an empty cache and a view matching live (version 100), when imported twice under --whatif, then the second run loads nothing in full.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task WholeRun_FullPreviewFindsViewUnchanged_RecordsItSoTheNextRunSkipsIt()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        var text = WriteView(ViewText());

        await RunAsync(client, cache, whatIf: true);
        var loadsAfterFirstRun = client.SavedQueryFullLoads;
        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(1, loadsAfterFirstRun, "Precondition: with nothing recorded, the first run should preview the view in full.");
        Expect.Equal(loadsAfterFirstRun, client.SavedQueryFullLoads, $"REQ-7: the second run should skip the view the first run found unchanged. Output: <{console.Output}>");
        Expect.Equal(new ImportCacheEntry(ImportAssetKind.View, ViewId, ImportCacheFixtures.Sha256Hex(text), 100), cache.Find(Environment, ImportAssetKind.View, ViewId), "REQ-7: the entry recorded should hold the file's hash and the live version read before the preview.");
    }

    // The cross-file duplicate check still sees a skipped file. Given two view files sharing a savedQueryId, one of which matches the cache,
    // then both are refused as before, instead of the skipped one quietly passing. REQ-8: "any doubt means full preview".
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task WholeRun_TwoViewFilesShareASavedQueryIdAndOneIsSkippable_StillFailsBoth()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), 100);
        WriteView(ViewText(description: "Left over from before the rename", name: "All projects (old)"), Path.Combine(_root, Table, "All projects (old).view.yml"));

        var exitCode = await RunAsync(client, cache, whatIf: true);

        Expect.Contains("0 changed, 0 unchanged, 2 failed", console.Output, "REQ-8: the duplicate-id check should still fail both files when one was skippable.");
        Expect.Equal(1, exitCode, "REQ-8: a run with failed files should exit non-zero.");
    }

    // ---- REQ-8: an edited file, or an asset changed in Dataverse, is previewed in full ----

    // REQ-8: "An edited file ... still gets the full preview."
    // Given the cache holds the hash of the file before an edit, when the edited file is imported, then it is previewed in full and written.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task WholeRun_EditedViewFile_GetsTheFullPreviewAndIsImported()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, ViewText(), versionNumber: 100);
        WriteView(ViewText(description: "Every project, newest first"));

        var exitCode = await RunAsync(client, cache);

        Expect.Equal(1, client.SavedQueryFullLoads, $"REQ-8: an edited view file should be previewed in full. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "REQ-8: the edited view should be written.");
        Expect.Contains("1 changed, 0 unchanged, 0 failed", console.Output, "REQ-8: the edited view should be counted as changed.");
        Expect.Equal(0, exitCode, "REQ-8: the import should succeed.");
    }

    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task WholeRun_EditedFormFile_GetsTheFullPreviewAndIsImported()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.Form, FormId, FormText(), versionNumber: 200);
        WriteForm(FormText(label: "Name (edited)"));

        var exitCode = await RunAsync(client, cache);

        Expect.Equal(1, client.SystemFormFullLoads, $"REQ-8: an edited form file should be previewed in full. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSystemFormXmlCalls.Count, "REQ-8: the edited form should be written.");
        Expect.Contains("1 changed, 0 unchanged, 0 failed", console.Output, "REQ-8: the edited form should be counted as changed.");
        Expect.Equal(0, exitCode, "REQ-8: the import should succeed.");
    }

    // REQ-8: "... a view or form changed in Dataverse since (versionnumber differs), still gets the full preview."
    // Given the file matches the cache but the live view is now at version 101 with a different description, then the full preview reports the change.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task WholeRunUnderWhatIf_ViewChangedInDataverse_GetsTheFullPreviewAndShowsTheChange()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        client.SavedQueriesById[ViewId] = client.SavedQueriesById[ViewId] with { Description = "Edited in the maker portal" };
        client.SavedQueryVersions[ViewId] = (Table, 101);
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), versionNumber: 100);

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(1, client.SavedQueryFullLoads, $"REQ-8: a view whose live version moved on should be previewed in full. Output: <{console.Output}>");
        Expect.Contains("1 would change, 0 unchanged, 0 failed", console.Output, "REQ-8: the drift made in Dataverse should be reported as a change, not skipped.");
    }

    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task WholeRunUnderWhatIf_FormChangedInDataverse_GetsTheFullPreviewAndShowsTheChange()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        client.SystemForm = LiveForm("Name (edited in the maker portal)");
        client.SystemFormVersions[FormId] = (Table, 201);
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.Form, FormId, WriteForm(FormText()), versionNumber: 200);

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(1, client.SystemFormFullLoads, $"REQ-8: a form whose live version moved on should be previewed in full. Output: <{console.Output}>");
        Expect.Contains("1 would change, 0 unchanged, 0 failed", console.Output, "REQ-8: the drift made in Dataverse should be reported as a change, not skipped.");
    }

    // ---- REQ-9: a different environment is never trusted ----

    // REQ-9: "Importing into a different environment from the one recorded gets the full preview."
    // Given the cache matches the file but was recorded against other.crm.dynamics.com, when importing into test.crm.dynamics.com, then the view is previewed in full.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task WholeRun_CacheRecordedForAnotherEnvironment_GetsTheFullPreview()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(OtherEnvironment, ImportAssetKind.View, ViewId, WriteView(ViewText()), versionNumber: 100);

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(1, client.SavedQueryFullLoads, $"REQ-9: an entry recorded for another environment should not let this one skip the preview. Output: <{console.Output}>");
    }

    // ---- REQ-10: no evidence means a full preview ----

    // REQ-10: "A missing or unreadable cache means the full preview, never 'unchanged'."
    // Given nothing recorded for the view, when imported, then it is previewed in full (and the preview, not the cache, says it is unchanged).
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task WholeRun_NoCacheEntryForTheView_GetsTheFullPreview()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        WriteView(ViewText());

        await RunAsync(client, new FakeImportCache(), whatIf: true);

        Expect.Equal(1, client.SavedQueryFullLoads, $"REQ-10: with no entry recorded the view should be previewed in full. Output: <{console.Output}>");
    }

    // Given the entry matches but Dataverse returns no version for the view (e.g. the query didn't list it), then there is no evidence and it is previewed in full.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task WholeRun_NoLiveVersionForTheView_GetsTheFullPreview()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        client.SavedQueryVersions.Clear();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), versionNumber: 100);

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(1, client.SavedQueryFullLoads, $"REQ-10: without a live version to compare, the view should be previewed in full. Output: <{console.Output}>");
    }

    // Given a hand-written view file with no savedQueryId, then it can't be matched to an entry and is previewed in full (here by its name).
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public async Task WholeRun_ViewFileWithoutASavedQueryId_GetsTheFullPreview()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        client.SavedQuery = client.SavedQueriesById[ViewId];
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, ViewText(), versionNumber: 100);
        WriteView(ViewYamlSerializer.ToYaml(new ViewDefinition { Name = ViewName, Entity = Table, Description = ViewDescription }));

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(1, client.SavedQueryFullLoads, $"REQ-10: a view file with no savedQueryId has no entry to match, so it should be previewed in full. Output: <{console.Output}>");
    }

    // ---- REQ-11: a successful import updates the cache ----

    // REQ-11: "After a successful import, the cache is updated so the next run skips what was just written."
    // Given an edited view that is applied (live version 100 -> 101 by the write), then the entry holds the file's hash and the post-write version 101,
    // so the tool's own write doesn't look like drift next time. Code path: StepRunner wraps the prepared apply and re-reads the version.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task WholeRun_AppliedView_RecordsItsHashWithTheVersionReadAfterTheWrite()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        var text = WriteView(ViewText(description: "Every project, newest first"));

        await RunAsync(client, cache);

        Expect.Equal(new ImportCacheEntry(ImportAssetKind.View, ViewId, ImportCacheFixtures.Sha256Hex(text), 101), cache.Find(Environment, ImportAssetKind.View, ViewId), $"REQ-11: the applied view should be recorded with its hash and the version re-read after the write (101, not the 100 read before). Output: <{console.Output}>");
    }

    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task WholeRun_AppliedForm_RecordsItsHashWithTheVersionReadAfterTheWrite()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        var text = WriteForm(FormText(label: "Name (edited)"));

        await RunAsync(client, cache);

        Expect.Equal(new ImportCacheEntry(ImportAssetKind.Form, FormId, ImportCacheFixtures.Sha256Hex(text), 201), cache.Find(Environment, ImportAssetKind.Form, FormId), $"REQ-11: the applied form should be recorded with its hash and the version re-read after the write (201, not the 200 read before). Output: <{console.Output}>");
    }

    // REQ-11: "... so the next run skips what was just written."
    // Given a view imported once, when the same folder is imported again, then the view is not previewed in full and is counted unchanged.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task WholeRun_ViewImportedOnce_IsSkippedByTheNextRun()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        WriteView(ViewText(description: "Every project, newest first"));
        await RunAsync(client, cache);
        var loadsAfterFirstRun = client.SavedQueryFullLoads;

        await RunAsync(client, cache);

        Expect.Equal(loadsAfterFirstRun, client.SavedQueryFullLoads, $"REQ-11: the view just written should not be previewed in full by the next run. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSavedQueryCalls.Count, "REQ-11: the second run should not write the view again.");
    }

    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task WholeRun_FormImportedOnce_IsSkippedByTheNextRun()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        WriteForm(FormText(label: "Name (edited)"));
        await RunAsync(client, cache);
        var loadsAfterFirstRun = client.SystemFormFullLoads;

        await RunAsync(client, cache);

        Expect.Equal(loadsAfterFirstRun, client.SystemFormFullLoads, $"REQ-11: the form just written should not be previewed in full by the next run. Output: <{console.Output}>");
        Expect.Equal(1, client.UpdateSystemFormXmlCalls.Count, "REQ-11: the second run should not write the form again.");
    }

    // REQ-11: only a write that happened is recorded. Under --whatif nothing is applied, so a changed view must stay previewable next time.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task WholeRunUnderWhatIf_ChangedView_RecordsNothingForIt()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        WriteView(ViewText(description: "Every project, newest first"));

        await RunAsync(client, cache, whatIf: true);

        Expect.Equal(0, cache.Entries.Count, $"REQ-11: --whatif writes nothing, so a changed view must not be recorded as matching. Output: <{console.Output}>");
    }

    // Declining the prompt writes nothing either, so the changed view is not recorded.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task WholeRun_ChangedViewWhenTheImportIsDeclined_RecordsNothingForIt()
    {
        using var console = new ConsoleHarness("n");
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        WriteView(ViewText(description: "Every project, newest first"));

        await RunAsync(client, cache, yes: false);

        Expect.Equal(0, client.UpdateSavedQueryCalls.Count, "Precondition: declining should write nothing.");
        Expect.Equal(0, cache.Entries.Count, $"REQ-11: a declined import wrote nothing, so the changed view must not be recorded as matching. Output: <{console.Output}>");
    }

    // REQ-11: a write Dataverse rejected didn't happen, so recording it would make the next run skip a file that was never applied.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public async Task WholeRun_ViewWriteFails_RecordsNothingForIt()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        client.WriteFailure = new HttpRequestException("Dataverse rejected the write.");
        var cache = new FakeImportCache();
        WriteView(ViewText(description: "Every project, newest first"));

        var exitCode = await RunAsync(client, cache);

        Expect.Equal(1, exitCode, "Precondition: a rejected write should fail the run.");
        Expect.Equal(0, cache.Entries.Count, $"REQ-11: a failed write must not be recorded, or the next run would skip a view that was never applied. Output: <{console.Output}>");
    }

    // ---- REQ-12: --no-skip ----

    // REQ-12: "--no-skip forces the full preview for every file."
    // Given the cache matches both files and Dataverse is unchanged, when imported with --no-skip, then the view and the form are both previewed in full.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task WholeRun_NoSkip_GetsTheFullPreviewForEveryFileEvenWhenTheCacheMatches()
    {
        using var console = new ConsoleHarness();
        var client = ClientWithLiveViewAndForm();
        var cache = new FakeImportCache();
        cache.Seed(Environment, ImportAssetKind.View, ViewId, WriteView(ViewText()), versionNumber: 100);
        cache.Seed(Environment, ImportAssetKind.Form, FormId, WriteForm(FormText()), versionNumber: 200);

        await RunAsync(client, cache, whatIf: true, noSkip: true);

        Expect.Equal(1, client.SavedQueryFullLoads, $"REQ-12: --no-skip should preview the view in full. Output: <{console.Output}>");
        Expect.Equal(1, client.SystemFormFullLoads, "REQ-12: --no-skip should preview the form in full.");
        Expect.Contains("0 changed, 2 unchanged, 0 failed", console.Output, "REQ-12: the previews find both unchanged, as they would without --no-skip.");
    }

    // REQ-12: the flag is on the command line.
    // Code path: ImportSolutionCommand.Settings.NoSkip, registered by SolutionCommands.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task SolutionImportHelp_ListsNoSkip()
    {
        using var console = new ConsoleHarness();
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.ConfigureConsole(console.Console);
            CommandAppConfiguration.Configure(config);
        });

        var exitCode = await app.RunAsync(["solution", "import", "--help"]);

        Expect.Equal(0, exitCode, $"'solution import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains("--no-skip", console.Output, "REQ-12: the help should list --no-skip.");
    }
}

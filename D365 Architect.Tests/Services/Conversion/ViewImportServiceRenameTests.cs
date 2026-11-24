using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Renaming a view by importing a file whose <c>savedQueryId</c> resolves to a live view with a different name
/// (<see cref="ViewImportService"/>, used by <c>view import</c> and <c>solution import</c>). Requirements REQ-1 to REQ-9 are in
/// each test's trait and comment; the command-level ones (diff, <c>--whatif</c>, confirmation, publishing) are in
/// <c>ImportViewCommandRenameTests</c> and <c>ImportSolutionCommandViewRenameTests</c>.
///
/// The contract these pin: <see cref="ViewImportPreview.ExistingName"/> is the live name (by-id lookup only) and the existing
/// <see cref="ViewImportPreview.Name"/> is the file's; a differing pair is one change in <see cref="ViewImportPreview.ChangeCount"/>;
/// <c>ApplyAsync</c> sends the name through <c>IDataverseClient.UpdateSavedQueryAsync(..., name, ...)</c> only when it is a rename
/// (null otherwise), together with whatever else changed in the same call; <see cref="ViewImportPreview.DuplicateNameWarning"/>
/// is set when <c>IDataverseClient.FindSavedQueryIdsByNameAsync</c> finds another view on the table with the new name.
/// </summary>
public sealed class ViewImportServiceRenameTests
{
    private static readonly Uri Environment = new("https://test.crm.dynamics.com");
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid OtherViewId = new("aaaaaaaa-0000-0000-0000-000000000002");

    private const string LiveName = "Active Accounts (LT)";
    private const string NewName = "Active Accounts Renamed";

    private static ExistingSavedQuery LiveView(string fetchXml = "<fetch>old</fetch>") =>
        new(ViewId, null, fetchXml, null, "account", LiveName);

    private static ViewDefinition RenamedView(string? fetchXml = null, string name = NewName, Guid? savedQueryId = null) => new()
    {
        Name = name,
        Entity = "account",
        SavedQueryId = savedQueryId ?? ViewId,
        FetchXml = fetchXml,
    };

    private static FakeDataverseClient ClientWithLiveView(string fetchXml = "<fetch>old</fetch>")
    {
        var client = new FakeDataverseClient();
        client.SavedQueriesById[ViewId] = LiveView(fetchXml);
        return client;
    }

    // ---- REQ-1: the name is written ----

    // REQ-1: "A file with a savedQueryId whose name differs from the live view renames that view."
    // Given a live view 'Active Accounts (LT)' and a file with the same savedQueryId and `view: Active Accounts Renamed`,
    // ViewImportService.ApplyAsync updates that view with name 'Active Accounts Renamed'.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task ApplyAsync_SavedQueryIdWithADifferentName_WritesTheNewNameToThatView()
    {
        var client = ClientWithLiveView();
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);
        await service.ApplyAsync(Environment, "token", preview, CancellationToken.None);

        var call = Assert.Single(client.UpdateSavedQueryCalls);
        Expect.Equal(ViewId, call.SavedQueryId, "REQ-1: the rename should go to the view the file's savedQueryId names.");
        Expect.Equal(NewName, call.Name, "REQ-1: the update should carry the file's view name as the new name; ViewImportService.ApplyAsync passes none today.");
    }

    // ---- REQ-2: the preview carries both names, for the diff ----

    // REQ-2: "The diff and --whatif show the rename (old and new name visible)."
    // The command prints these two values; ViewImportService.PreviewAsync must expose them on the ViewImportPreview.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public async Task PreviewAsync_Rename_ExposesTheLiveNameAndTheFilesName()
    {
        var service = new ViewImportService(ClientWithLiveView());

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);

        Expect.Equal(LiveName, preview.ExistingName, "REQ-2: the preview should carry the live view's name so the diff can show the old name.");
        Expect.Equal(NewName, preview.Name, "REQ-2: the preview should carry the file's name so the diff can show the new name.");
    }

    // ---- REQ-3: a rename alone is a change ----

    // REQ-3: "A rename on its own counts as a change, so it's confirmed and published like any other (today it reports no changes)."
    // Given a file that differs from the live view only by name, ViewImportPreview.HasChanges is true and ChangeCount is 1.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public async Task PreviewAsync_NameIsTheOnlyDifference_CountsAsOneChange()
    {
        var service = new ViewImportService(ClientWithLiveView());

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);

        Expect.True(preview.HasChanges, "REQ-3: a rename on its own should be a change (ViewImportPreview.HasChanges ignores the name today).");
        Expect.Equal(1, preview.ChangeCount, "REQ-3: a rename on its own should count as exactly one change.");
    }

    // ---- REQ-4: one update ----

    // REQ-4: "A rename plus FetchXML/layout changes in the same file are applied together in one update."
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task ApplyAsync_RenameAndFetchXmlChange_AreWrittenInOneUpdate()
    {
        var client = ClientWithLiveView(fetchXml: "<fetch>old</fetch>");
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(fetchXml: "<fetch>new</fetch>"), CancellationToken.None);
        await service.ApplyAsync(Environment, "token", preview, CancellationToken.None);

        var call = Assert.Single(client.UpdateSavedQueryCalls);
        Expect.Equal(NewName, call.Name, "REQ-4: the single update should carry the new name.");
        Expect.Equal("<fetch>new</fetch>", call.FetchXml, "REQ-4: the single update should carry the changed FetchXML too.");
    }

    // REQ-4 (counting): the rename and the FetchXML change are two changes in the preview.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task PreviewAsync_RenameAndFetchXmlChange_CountsTwoChanges()
    {
        var service = new ViewImportService(ClientWithLiveView(fetchXml: "<fetch>old</fetch>"));

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(fetchXml: "<fetch>new</fetch>"), CancellationToken.None);

        Expect.Equal(2, preview.ChangeCount, "REQ-4: the name and the FetchXML both differ, so two changes were expected.");
    }

    // ---- REQ-5: no savedQueryId, matched by name, so no rename ----

    // REQ-5: "A file without savedQueryId is still matched by name, so it can't rename."
    // Given a by-name match whose FetchXML changes, the update leaves the name alone (null), as today. Guards the existing behaviour.
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public async Task ApplyAsync_FileWithoutSavedQueryId_UpdatesTheNameMatchedViewWithoutSendingAName()
    {
        var client = new FakeDataverseClient { SavedQuery = new ExistingSavedQuery(ViewId, null, "<fetch>old</fetch>", null) };
        var service = new ViewImportService(client);
        var file = new ViewDefinition { Name = NewName, Entity = "account", FetchXml = "<fetch>new</fetch>" };

        var preview = await service.PreviewAsync(Environment, "token", file, CancellationToken.None);
        await service.ApplyAsync(Environment, "token", preview, CancellationToken.None);

        var call = Assert.Single(client.UpdateSavedQueryCalls);
        Expect.Equal(null, call.Name, "REQ-5: a by-name match is the same name by definition, so no name should be sent.");
        Expect.Equal(1, preview.ChangeCount, "REQ-5: only the FetchXML differs for a by-name match.");
    }

    // REQ-5: "name matching nothing is a create, as today" - an old-named view with a different id elsewhere doesn't turn it into a rename.
    // Guards the existing behaviour: ViewImportService.PreviewAsync -> IsCreate, ApplyAsync -> CreateSavedQueryAsync, never UpdateSavedQueryAsync.
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public async Task ApplyAsync_FileWithoutSavedQueryIdWhoseNameMatchesNothing_CreatesAViewAndRenamesNothing()
    {
        var client = new FakeDataverseClient { SavedQuery = null };
        client.SavedQueriesById[ViewId] = LiveView();
        var service = new ViewImportService(client);
        var file = new ViewDefinition
        {
            Name = NewName,
            Entity = "account",
            FetchXml = "<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>",
            LayoutXml = "<grid name=\"resultset\" object=\"1\" jump=\"name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"accountid\"><cell name=\"name\" width=\"300\" /></row></grid>",
        };

        var preview = await service.PreviewAsync(Environment, "token", file, CancellationToken.None);
        await service.ApplyAsync(Environment, "token", preview, CancellationToken.None);

        Expect.True(preview.IsCreate, "REQ-5: a name that matches no view, with no savedQueryId, should still be a create.");
        Expect.Equal(1, client.CreateSavedQueryCalls.Count, "REQ-5: the new view should be created.");
        Expect.Empty(client.UpdateSavedQueryCalls, "REQ-5: no existing view should be updated or renamed.");
    }

    // ---- REQ-6: a different table is still refused ----
    // The id-on-another-table refusal (ViewIdentityMismatchException) is covered by
    // ViewImportServiceTests.PreviewAsync_WithSavedQueryIdOnADifferentEntity_RefusesNamingBoth; this one pins that a rename can't slip past it.

    // REQ-6: "an id that belongs to a view on a different table is still refused (ViewIdentityMismatchException)."
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task PreviewAsync_RenamingAViewIdFromAnotherTable_IsRefusedAndWritesNothing()
    {
        var client = new FakeDataverseClient();
        client.SavedQueriesById[ViewId] = new ExistingSavedQuery(ViewId, null, "<fetch/>", null, "contact", "Active Contacts");
        var service = new ViewImportService(client);

        await Assert.ThrowsAsync<ViewIdentityMismatchException>(() =>
            service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None));

        Expect.Empty(client.UpdateSavedQueryCalls, "REQ-6: nothing should be written for a view id that belongs to another table.");
    }

    // ---- REQ-8: re-importing after the rename is quiet ----

    // REQ-8: "Importing the same file again after a rename shows no changes."
    // The fake keeps what an update wrote for a view looked up by id, as Dataverse does.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task PreviewAsync_ImportingTheSameFileAfterTheRename_HasNoChanges()
    {
        var client = ClientWithLiveView();
        var service = new ViewImportService(client);
        var file = RenamedView();
        var first = await service.PreviewAsync(Environment, "token", file, CancellationToken.None);
        await service.ApplyAsync(Environment, "token", first, CancellationToken.None);

        var second = await service.PreviewAsync(Environment, "token", file, CancellationToken.None);

        Expect.Equal(NewName, second.ExistingName, "REQ-8: after the rename the live view should report the new name; the first import did not write it.");
        Expect.True(!second.HasChanges, "REQ-8: importing the same file again should show no changes.");
    }

    // ---- REQ-9: a name another view already uses is a warning, not a refusal ----

    // REQ-9: "Renaming to a name another view on the same table already uses goes ahead, with a warning."
    // Given another account view is already called 'Active Accounts Renamed', ViewImportService.PreviewAsync sets DuplicateNameWarning
    // (naming the new name) and still previews the rename.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task PreviewAsync_NewNameIsUsedByAnotherViewOnTheTable_WarnsAndStillPreviewsTheRename()
    {
        var client = ClientWithLiveView();
        client.NamedSavedQueries.Add((OtherViewId, "account", NewName));
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);

        Expect.True(preview.DuplicateNameWarning is not null, "REQ-9: a new name already used by another view on the table should produce a DuplicateNameWarning.");
        Expect.Contains($"'{NewName}'", preview.DuplicateNameWarning, "REQ-9: the warning should name the duplicated view name in single quotes.");
        Expect.True(preview.HasChanges, "REQ-9: the rename should still be previewed as a change.");
    }

    // REQ-9: the rename "goes ahead" - the name is written even though it is a duplicate.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task ApplyAsync_NewNameIsUsedByAnotherViewOnTheTable_StillWritesTheName()
    {
        var client = ClientWithLiveView();
        client.NamedSavedQueries.Add((OtherViewId, "account", NewName));
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);
        await service.ApplyAsync(Environment, "token", preview, CancellationToken.None);

        Expect.Equal(NewName, Assert.Single(client.UpdateSavedQueryCalls).Name, "REQ-9: Dataverse allows duplicate view names, so the rename should be written.");
    }

    // REQ-9: no other view has the name, so there is nothing to warn about.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task PreviewAsync_NewNameIsUnused_HasNoDuplicateWarning()
    {
        var service = new ViewImportService(ClientWithLiveView());

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);

        Expect.True(preview.DuplicateNameWarning is null, $"REQ-9: no other view uses '{NewName}', so no warning was expected, but got: <{preview.DuplicateNameWarning}>.");
        Expect.True(preview.HasChanges, "REQ-9 (set-up): the rename should be previewed, otherwise this test proves nothing.");
    }

    // REQ-9: "on the same table" - a view of that name on another table doesn't clash.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task PreviewAsync_NewNameIsUsedOnlyOnAnotherTable_HasNoDuplicateWarning()
    {
        var client = ClientWithLiveView();
        client.NamedSavedQueries.Add((OtherViewId, "contact", NewName));
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);

        Expect.True(preview.DuplicateNameWarning is null, $"REQ-9: a contact view called '{NewName}' doesn't clash with an account view, so no warning was expected, but got: <{preview.DuplicateNameWarning}>.");
        Expect.True(preview.HasChanges, "REQ-9 (set-up): the rename should be previewed, otherwise this test proves nothing.");
    }

    // REQ-9: the view being renamed isn't "another view". A case-only rename can find the view itself under its new spelling.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task PreviewAsync_OnlyTheViewItselfHasTheNewName_HasNoDuplicateWarning()
    {
        var client = ClientWithLiveView();
        client.NamedSavedQueries.Add((ViewId, "account", NewName));
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", RenamedView(), CancellationToken.None);

        Expect.True(preview.DuplicateNameWarning is null, $"REQ-9: the only view found under '{NewName}' is the one being renamed, so no warning was expected, but got: <{preview.DuplicateNameWarning}>.");
        Expect.True(preview.HasChanges, "REQ-9 (set-up): the rename should be previewed, otherwise this test proves nothing.");
    }
}

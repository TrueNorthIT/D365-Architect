using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// REQ-22 and REQ-23 for views: <see cref="ViewImportService"/> creates a view
/// that matches nothing, and adds whatever it creates or changes to the
/// solution it is given. The create body is Microsoft Learn's: <c>POST
/// savedqueries</c> with <c>name</c>, <c>returnedtypecode</c>,
/// <c>fetchxml</c>, <c>layoutxml</c> and <c>querytype</c> 0 ("Customize Views
/// in Model-driven Apps", Create views:
/// https://learn.microsoft.com/power-apps/developer/model-driven-apps/customize-entity-views ).
/// </summary>
public sealed class ViewImportServiceCreateTests
{
    private static ViewDefinition RawView(string name = "All Invoice Centres", string? description = null, Guid? savedQueryId = null) =>
        ViewOf(name: name, description: description, savedQueryId: savedQueryId, fetchXml: ExportedFetchXml, layoutXml: ExportedLayoutXml());

    private static Task<ViewImportPreview> Preview(FakeDataverseClient client, ViewDefinition view) =>
        new ViewImportService(client).PreviewAsync(EnvironmentUrl, "token", view, CancellationToken.None);

    private static async Task<FakeDataverseClient> Create(ViewDefinition view, string? solution = null)
    {
        var client = ClientWithTable();
        var service = new ViewImportService(client);
        var preview = await service.PreviewAsync(EnvironmentUrl, "token", view, CancellationToken.None);
        await service.ApplyAsync(EnvironmentUrl, "token", preview, solution, CancellationToken.None);
        return client;
    }

    // ---- REQ-23: a view that matches nothing is created ----

    // REQ-23 example: `+ view 'All Invoice Centres' (create)`. No savedQueryId and no view of that name on that table -> a create, with changes to show.
    // Code path: ViewImportService.PreviewAsync (today throws ViewNotFoundException here).
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task PreviewAsync_NoSavedQueryIdAndNoViewOfThatNameOnThatTable_IsACreate()
    {
        var preview = await Preview(ClientWithTable(), RawView());

        Expect.True(preview.IsCreate, "REQ-23: a view file matching no existing view (by id, or by name on that table) should preview as a create, not throw ViewNotFoundException.");
        Expect.True(preview.HasChanges, "REQ-23: a create is always a change to show and confirm.");
    }

    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task PreviewAsync_ViewOfThatNameExistsOnThatTable_IsAnUpdateNotACreate()
    {
        var client = ClientWithTable();
        client.SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), null, "<fetch>old</fetch>", "<grid/>");

        var preview = await Preview(client, RawView());

        Expect.True(!preview.IsCreate, "REQ-23: a view that already exists by name on that table should still be an update.");
    }

    // REQ-23 + the existing rule "a stale id never falls back to the name": a savedQueryId that resolves to nothing is still an error, never a create.
    // (Pinned by ViewImportServiceTests.PreviewAsync_WithSavedQueryIdNotFoundLive_ThrowsViewNotFoundException, which must keep passing.)
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task PreviewAsync_SavedQueryIdThatResolvesToNothing_IsStillAnErrorNeverACreate()
    {
        var client = ClientWithTable();

        await Assert.ThrowsAsync<ViewNotFoundException>(() => Preview(client, RawView(savedQueryId: Guid.NewGuid())));
        Expect.Empty(client.CreateSavedQueryCalls, "REQ-23: a stale id must never create a view.");
    }

    // The body Learn documents. Code path: ViewImportService.ApplyAsync(..., solutionUniqueName, ...) -> IDataverseClient.CreateSavedQueryAsync.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task ApplyAsync_Create_PostsNameTableFetchLayoutAndQueryTypeZero()
    {
        var client = await Create(RawView());

        var call = Assert.Single(client.CreateSavedQueryCalls);
        Expect.Equal("All Invoice Centres", (string?)call.Body["name"], "REQ-23: the created view should carry the file's name.");
        Expect.Equal(TableName, (string?)call.Body["returnedtypecode"], "REQ-23: 'returnedtypecode' should be the table's logical name.");
        Expect.Equal(ExportedFetchXml, (string?)call.Body["fetchxml"], "REQ-23: 'fetchxml' should be the file's FetchXML.");
        Expect.Equal(ExportedLayoutXml(), (string?)call.Body["layoutxml"], "REQ-23: 'layoutxml' should be the file's layout (its object already right).");
        Expect.Equal(0, (int?)call.Body["querytype"], "REQ-23: 'querytype' must be 0 (a public view) - Learn: 'Must always be zero (0)'.");
    }

    // Omit-when-absent: no description in the file means no description key in the body.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task ApplyAsync_CreateWithoutADescription_SendsNoDescription()
    {
        var client = await Create(RawView());

        Expect.True(!Assert.Single(client.CreateSavedQueryCalls).Body.ContainsKey("description"), "REQ-23: with no description in the file, the create body should have no 'description' key.");
    }

    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task ApplyAsync_CreateWithADescription_SendsIt()
    {
        var client = await Create(RawView(description: "Every invoice centre"));

        Expect.Equal("Every invoice centre", (string?)Assert.Single(client.CreateSavedQueryCalls).Body["description"], "REQ-23: the file's description should be in the create body.");
    }

    // A create from `columns:` writes the layout and FetchXML the preview built (REQ-24/26/27 reach Dataverse).
    [Fact]
    [Trait("Requirement", "REQ-24")]
    public async Task ApplyAsync_CreateFromColumns_PostsTheGeneratedLayoutAndFetchXml()
    {
        var client = ClientWithTable();
        var service = new ViewImportService(client);
        var preview = await service.PreviewAsync(EnvironmentUrl, "token", ViewOf(columns: [Col("bogdan_name", 300), Col("createdon")]), CancellationToken.None);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, solutionUniqueName: null, CancellationToken.None);

        var body = Assert.Single(client.CreateSavedQueryCalls).Body;
        Expect.Equal(preview.NewLayoutXml, (string?)body["layoutxml"], "REQ-24: the posted layout should be the one the plan showed.");
        Expect.Equal(preview.NewFetchXml, (string?)body["fetchxml"], "REQ-26: the posted FetchXML should be the one the plan showed.");
        Expect.Equal(LiveTypeCode.ToString(), Grid((string)body["layoutxml"]!).GridAttributes.GetValueOrDefault("object"), "REQ-27: the posted layout should carry the live type code.");
    }

    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task ApplyAsync_Create_UpdatesNoExistingView()
    {
        var client = await Create(RawView());

        Expect.Empty(client.UpdateSavedQueryCalls, "REQ-23: creating a view must not PATCH any existing view.");
    }

    // ---- REQ-22: --solution ----

    // REQ-22 example: `view import -i all.view.yml --solution TableCreateTest` -> the view appears in the solution.
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task ApplyAsync_CreateWithASolution_CreatesTheViewInThatSolution()
    {
        var client = await Create(RawView(), solution: "TableCreateTest");

        Expect.Equal("TableCreateTest", Assert.Single(client.CreateSavedQueryCalls).SolutionUniqueName, "REQ-22: the create should carry 'TableCreateTest' as the solution (MSCRM.SolutionUniqueName).");
    }

    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task ApplyAsync_CreateWithoutASolution_SendsNoSolution()
    {
        var client = await Create(RawView(), solution: null);

        Expect.Equal(null, Assert.Single(client.CreateSavedQueryCalls).SolutionUniqueName, "REQ-22: with no --solution the create should name no solution.");
    }

    // REQ-22: "anything they create or change is added to that solution" - the change half. Learn: Update (PATCH) accepts MSCRM.SolutionUniqueName.
    [Fact]
    [Trait("Requirement", "REQ-22")]
    public async Task ApplyAsync_UpdateWithASolution_ChangesTheViewInThatSolution()
    {
        var client = ClientWithTable();
        var viewId = Guid.NewGuid();
        client.SavedQuery = new ExistingSavedQuery(viewId, null, "<fetch>old</fetch>", ExportedLayoutXml());
        var service = new ViewImportService(client);
        var preview = await service.PreviewAsync(EnvironmentUrl, "token", RawView(), CancellationToken.None);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, "TableCreateTest", CancellationToken.None);

        var call = Assert.Single(client.UpdateSavedQueryCalls);
        Expect.Equal(viewId, call.SavedQueryId, "REQ-22: the existing view should be the one updated.");
        Expect.Equal("TableCreateTest", call.SolutionUniqueName, "REQ-22: a changed view should be added to the solution the import was given.");
        Expect.Empty(client.CreateSavedQueryCalls, "REQ-22: an update must not create a second view.");
    }

    // ---- Refusals on a create (no silent fixes) ----

    // A new view needs something to show. With neither `columns` nor `layoutXml` there is nothing to build a layout from.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task PreviewAsync_NewViewWithNeitherColumnsNorLayoutXml_IsRefusedNamingTheView()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Preview(ClientWithTable(), ViewOf(fetchXml: ExportedFetchXml)));

        Expect.Contains("All Invoice Centres", ex.Message, "REQ-23: the refusal should name the view that can't be created.");
        Expect.Contains("columns", ex.Message, "REQ-23: and say a new view needs 'columns' (or a layoutXml).");
    }

    // With a raw layoutXml (no columns) there is nothing to derive the query from either.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task PreviewAsync_NewViewWithRawLayoutXmlButNoFetchXml_IsRefusedNamingFetchXml()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Preview(ClientWithTable(), ViewOf(layoutXml: ExportedLayoutXml())));

        Expect.Contains("fetchXml", ex.Message, "REQ-23: a new view with a raw layoutXml needs a fetchXml, and the refusal should say so.");
    }

    // Learn: only a public view (querytype 0) can be created; the others are update-only.
    [Fact]
    [Trait("Requirement", "REQ-23")]
    public async Task PreviewAsync_NewViewOfANonPublicQueryType_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Preview(ClientWithTable(), ViewOf(queryType: "QuickFindSearch", fetchXml: ExportedFetchXml, layoutXml: ExportedLayoutXml())));

        Expect.Contains("QuickFindSearch", ex.Message, "REQ-23: only a public view can be created (Learn: querytype 'Must always be zero'); the refusal should name the unsupported type.");
    }
}

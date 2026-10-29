using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="TableCreateService"/> (the <see cref="ITableCreateService"/>
/// behind <c>table create</c>) against a <see cref="FakeDataverseClient"/>:
/// previewing writes nothing, applying creates the table and only the table,
/// an existing table is left alone, the solution is threaded through, and a
/// rejected create is reported by table name. Preview/apply mirror
/// <see cref="ITableImportService"/>'s <see cref="IImportService{TInput,TPreview}"/> shape.
/// </summary>
public sealed class TableCreateServiceTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    private static (TableCreateService Service, FakeDataverseClient Client) CreateService(bool tableExistsLive)
    {
        var client = new FakeDataverseClient { EntityExists = _ => tableExistsLive };
        return (new TableCreateService(client), client);
    }

    private static Task<TableCreatePreview> Preview(TableCreateService service, EntityDefinition? entity = null) =>
        service.PreviewAsync(EnvironmentUrl, "token", entity ?? TableCreateFixtures.Table(), CancellationToken.None);

    // ---- REQ-1: creates a table from the file when it doesn't exist ----

    // REQ-1: "A new `table create` command creates a table from a `*.table.yml` when it doesn't exist in Dataverse yet."
    // Code path: TableCreateService.PreviewAsync -> IDataverseClient.EntityExistsAsync, EntityMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task PreviewAsync_TableNotLive_PlansACreateWithTheBody()
    {
        var (service, _) = CreateService(tableExistsLive: false);

        var preview = await Preview(service);

        Expect.Equal(false, preview.AlreadyExists, "REQ-1: a table that isn't live should not preview as already existing.");
        Expect.True(preview.HasChanges, "REQ-1: a table that isn't live should preview as having something to create.");
        Expect.Equal("tn_Project", (string?)preview.RequestBody?["SchemaName"], "REQ-1: the preview should carry the EntityMetadata body that would be created.");
    }

    // REQ-1: existence is checked by the table's logical name from the file.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task PreviewAsync_ChecksExistenceOfTheFilesLogicalName()
    {
        var (service, client) = CreateService(tableExistsLive: false);

        await Preview(service);

        Expect.Sequence(["tn_project"], client.EntityExistsCalls, "REQ-1: the service should ask Dataverse whether 'tn_project' exists before planning a create.");
    }

    // REQ-7: "--whatif prints the planned table + primary column, writes nothing" - the service half: preview never writes.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public async Task PreviewAsync_NeverWritesAnything()
    {
        var (service, client) = CreateService(tableExistsLive: false);

        await Preview(service);

        Expect.Empty(client.CreateEntityCalls, "REQ-7: previewing should not create the table.");
        Expect.Empty(client.ExecuteTransactionCalls, "REQ-7: previewing should not send any transaction.");
    }

    // REQ-1: applying the preview creates the table once, with the previewed body.
    // Code path: TableCreateService.ApplyAsync (4 args, from IImportService) -> IDataverseClient.CreateEntityAsync.
    [Fact]
    [Trait("Requirement", "REQ-1")]
    public async Task ApplyAsync_CreatesTheTableOnceWithThePreviewedBody()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal(preview.RequestBody!.ToJsonString(), call.Body.ToJsonString(), "REQ-1: the body sent to Dataverse should be the one previewed.");
        Expect.Equal(null, call.SolutionUniqueName, "REQ-1: with no solution given, no solution should be sent.");
    }

    // ---- REQ-2: only the table and its primary name column ----

    // REQ-2: "File lists tn_name, tn_budget, tn_startdate; after `table create` only `tn_name` exists."
    // The preview says which columns are left for `table import`.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public async Task PreviewAsync_ReportsThePrimaryColumnAndTheColumnsLeftForTableImport()
    {
        var (service, _) = CreateService(tableExistsLive: false);

        var preview = await Preview(service);

        Expect.Equal("tn_name", preview.PrimaryNameColumn, "REQ-2: the preview should name the primary name column that will be created.");
        Expect.Sequence(["tn_budget", "tn_startdate"], preview.DeferredColumns, "REQ-2: the other columns in the file should be listed as left for 'table import'.");
    }

    // REQ-2: applying creates the table and nothing else - no column, relationship or table-update writes.
    [Fact]
    [Trait("Requirement", "REQ-2")]
    public async Task ApplyAsync_WritesNoColumnsRelationshipsOrTableUpdates()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        Expect.Empty(client.CreateAttributeCalls, "REQ-2: table create should not create tn_budget or tn_startdate (that is table import's job).");
        Expect.Empty(client.CreateOneToManyRelationshipCalls, "REQ-2: table create should not create relationships.");
        Expect.Empty(client.UpdateEntityCalls, "REQ-2: table create should not update a table.");
    }

    // ---- REQ-4 / REQ-5: refusals happen at preview, before any write ----

    // REQ-4 example: Missing `ownershipType` -> stops with "ownershipType is required to create tn_project", nothing written.
    [Fact]
    [Trait("Requirement", "REQ-4")]
    public async Task PreviewAsync_MissingOwnershipType_ThrowsAndWritesNothing()
    {
        var (service, client) = CreateService(tableExistsLive: false);

        var ex = await Assert.ThrowsAsync<TableCreateValidationException>(() => Preview(service, TableCreateFixtures.Table(ownershipType: null)));

        Expect.Contains("ownershipType is required to create tn_project", ex.Message, "REQ-4: a file with no ownershipType should be refused at preview.");
        Expect.Empty(client.CreateEntityCalls, "REQ-4: nothing should be written when the file is refused.");
    }

    // REQ-5 example: `isActivity: true` -> "Activity tables aren't supported by table create yet".
    [Fact]
    [Trait("Requirement", "REQ-5")]
    public async Task PreviewAsync_ActivityTable_IsRefused()
    {
        var (service, _) = CreateService(tableExistsLive: false);

        var ex = await Assert.ThrowsAsync<TableCreateValidationException>(() => Preview(service, TableCreateFixtures.Table(isActivity: true)));

        Expect.Contains("Activity tables aren't supported by table create yet", ex.Message, "REQ-5: an activity table should be refused at preview.");
    }

    // ---- REQ-6: already exists -> say so, change nothing ----

    // REQ-6: "If the table already exists, the command says so and changes nothing."
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task PreviewAsync_TableAlreadyLive_IsAlreadyExistsWithNothingToCreate()
    {
        var (service, _) = CreateService(tableExistsLive: true);

        var preview = await Preview(service);

        Expect.Equal(true, preview.AlreadyExists, "REQ-6: a live table should preview as already existing.");
        Expect.Equal(false, preview.HasChanges, "REQ-6: an existing table has nothing to create.");
        Expect.Equal(null, preview.RequestBody, "REQ-6: no create body should be built for an existing table.");
    }

    // REQ-6 (and REQ-10): a file for a table that already exists is not validated - older exports have no
    // `primaryNameAttribute`, and `solution import` must not start failing on tables it would never create.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task PreviewAsync_TableAlreadyLiveAndFileLacksCreateKeys_IsStillJustAlreadyExists()
    {
        var (service, _) = CreateService(tableExistsLive: true);
        var oldExport = TableCreateFixtures.Table(ownershipType: null, primaryNameAttribute: null);

        var preview = await Preview(service, oldExport);

        Expect.Equal(true, preview.AlreadyExists, "REQ-6: an existing table should be reported as existing even when the file couldn't be used to create it.");
    }

    // REQ-6: applying an already-exists preview writes nothing.
    [Fact]
    [Trait("Requirement", "REQ-6")]
    public async Task ApplyAsync_AlreadyExistsPreview_WritesNothing()
    {
        var (service, client) = CreateService(tableExistsLive: true);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, "TNCore", CancellationToken.None);

        Expect.Empty(client.CreateEntityCalls, "REQ-6: applying a preview for an existing table should not create anything.");
        Expect.Empty(client.ExecuteTransactionCalls, "REQ-6: applying a preview for an existing table should not send a transaction.");
    }

    // ---- REQ-8: the new table joins the solution as it is created ----

    // REQ-8: "`--solution` adds the new table to that solution as it's created."
    // Example: with `--solution TNCore`, a later `solution export TNCore` includes `tn_project`.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public async Task ApplyAsync_WithSolution_CreatesTheTableInThatSolution()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, "TNCore", CancellationToken.None);

        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal("TNCore", call.SolutionUniqueName, "REQ-8: the table should be created with solution 'TNCore' so a later solution export includes it.");
    }

    // ---- REQ-12: a rejected create names the table ----

    // REQ-12: "If creating a table fails, later stages stop and say which table failed."
    // Example: Creation of `tn_project` rejected -> error names `tn_project`.
    [Fact]
    [Trait("Requirement", "REQ-12")]
    public async Task ApplyAsync_DataverseRejectsTheCreate_ThrowsAnErrorNamingTheTable()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        client.CreateEntityFailure = new HttpRequestException("Dataverse request failed (400 BadRequest): schema name is already in use");
        var preview = await Preview(service);

        var ex = await Assert.ThrowsAsync<TableCreateFailedException>(() => service.ApplyAsync(EnvironmentUrl, "token", preview, "TNCore", CancellationToken.None));

        Expect.Equal("tn_project", ex.EntityLogicalName, "REQ-12: the failure should say which table could not be created.");
        Expect.Contains("tn_project", ex.Message, "REQ-12: the error message should name tn_project.");
        Expect.Contains("schema name is already in use", ex.Message, "REQ-12: the error message should keep Dataverse's own reason.");
    }
}

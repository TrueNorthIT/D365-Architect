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

    // ---- creates a table from the file when it doesn't exist ----

    // "A new `table create` command creates a table from a `*.table.yml` when it doesn't exist in Dataverse yet."
    // Code path: TableCreateService.PreviewAsync -> IDataverseClient.EntityExistsAsync, EntityMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    public async Task PreviewAsync_TableNotLive_PlansACreateWithTheBody()
    {
        var (service, _) = CreateService(tableExistsLive: false);

        var preview = await Preview(service);

        Expect.Equal(false, preview.AlreadyExists, "a table that isn't live should not preview as already existing.");
        Expect.True(preview.HasChanges, "a table that isn't live should preview as having something to create.");
        Expect.Equal("tn_Project", (string?)preview.RequestBody?["SchemaName"], "the preview should carry the EntityMetadata body that would be created.");
    }

    // Existence is checked by the table's logical name from the file.
    [Fact]
    public async Task PreviewAsync_ChecksExistenceOfTheFilesLogicalName()
    {
        var (service, client) = CreateService(tableExistsLive: false);

        await Preview(service);

        Expect.Sequence(["tn_project"], client.EntityExistsCalls, "the service should ask Dataverse whether 'tn_project' exists before planning a create.");
    }

    // "--whatif prints the planned table + primary column, writes nothing" - the service half: preview never writes.
    [Fact]
    public async Task PreviewAsync_NeverWritesAnything()
    {
        var (service, client) = CreateService(tableExistsLive: false);

        await Preview(service);

        Expect.Empty(client.CreateEntityCalls, "previewing should not create the table.");
        Expect.Empty(client.ExecuteTransactionCalls, "previewing should not send any transaction.");
    }

    // Applying the preview creates the table once, with the previewed body.
    // Code path: TableCreateService.ApplyAsync (4 args, from IImportService) -> IDataverseClient.CreateEntityAsync.
    [Fact]
    public async Task ApplyAsync_CreatesTheTableOnceWithThePreviewedBody()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal(preview.RequestBody!.ToJsonString(), call.Body.ToJsonString(), "the body sent to Dataverse should be the one previewed.");
        Expect.Equal(null, call.SolutionUniqueName, "with no solution given, no solution should be sent.");
    }

    // ---- only the table and its primary name column ----

    // "File lists tn_name, tn_budget, tn_startdate; after `table create` only `tn_name` exists."
    // The preview says which columns are left for `table import`.
    [Fact]
    public async Task PreviewAsync_ReportsThePrimaryColumnAndTheColumnsLeftForTableImport()
    {
        var (service, _) = CreateService(tableExistsLive: false);

        var preview = await Preview(service);

        Expect.Equal("tn_name", preview.PrimaryNameColumn, "the preview should name the primary name column that will be created.");
        Expect.Sequence(["tn_budget", "tn_startdate"], preview.DeferredColumns, "the other columns in the file should be listed as left for 'table import'.");
    }

    // Applying creates the table and nothing else - no column, relationship or table-update writes.
    [Fact]
    public async Task ApplyAsync_WritesNoColumnsRelationshipsOrTableUpdates()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        Expect.Empty(client.CreateAttributeCalls, "table create should not create tn_budget or tn_startdate (that is table import's job).");
        Expect.Empty(client.CreateOneToManyRelationshipCalls, "table create should not create relationships.");
        Expect.Empty(client.UpdateEntityCalls, "table create should not update a table.");
    }

    // ---- refusals happen at preview, before any write ----

    // Example: Missing `ownershipType` -> stops with "ownershipType is required to create tn_project", nothing written.
    [Fact]
    public async Task PreviewAsync_MissingOwnershipType_ThrowsAndWritesNothing()
    {
        var (service, client) = CreateService(tableExistsLive: false);

        var ex = await Assert.ThrowsAsync<TableCreateValidationException>(() => Preview(service, TableCreateFixtures.Table(ownershipType: null)));

        Expect.Contains("ownershipType is required to create tn_project", ex.Message, "a file with no ownershipType should be refused at preview.");
        Expect.Empty(client.CreateEntityCalls, "nothing should be written when the file is refused.");
    }

    // Example: `isActivity: true` -> "Activity tables aren't supported by table create yet".
    [Fact]
    public async Task PreviewAsync_ActivityTable_IsRefused()
    {
        var (service, _) = CreateService(tableExistsLive: false);

        var ex = await Assert.ThrowsAsync<TableCreateValidationException>(() => Preview(service, TableCreateFixtures.Table(isActivity: true)));

        Expect.Contains("Activity tables aren't supported by table create yet", ex.Message, "an activity table should be refused at preview.");
    }

    // ---- already exists -> say so, change nothing ----

    // "If the table already exists, the command says so and changes nothing."
    [Fact]
    public async Task PreviewAsync_TableAlreadyLive_IsAlreadyExistsWithNothingToCreate()
    {
        var (service, _) = CreateService(tableExistsLive: true);

        var preview = await Preview(service);

        Expect.Equal(true, preview.AlreadyExists, "a live table should preview as already existing.");
        Expect.Equal(false, preview.HasChanges, "an existing table has nothing to create.");
        Expect.Equal(null, preview.RequestBody, "no create body should be built for an existing table.");
    }

    // A file for a table that already exists is not validated - older exports have no
    // `primaryNameAttribute`, and `solution import` must not start failing on tables it would never create.
    [Fact]
    public async Task PreviewAsync_TableAlreadyLiveAndFileLacksCreateKeys_IsStillJustAlreadyExists()
    {
        var (service, _) = CreateService(tableExistsLive: true);
        var oldExport = TableCreateFixtures.Table(ownershipType: null, primaryNameAttribute: null);

        var preview = await Preview(service, oldExport);

        Expect.Equal(true, preview.AlreadyExists, "an existing table should be reported as existing even when the file couldn't be used to create it.");
    }

    // Applying an already-exists preview writes nothing.
    [Fact]
    public async Task ApplyAsync_AlreadyExistsPreview_WritesNothing()
    {
        var (service, client) = CreateService(tableExistsLive: true);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, "TNCore", CancellationToken.None);

        Expect.Empty(client.CreateEntityCalls, "applying a preview for an existing table should not create anything.");
        Expect.Empty(client.ExecuteTransactionCalls, "applying a preview for an existing table should not send a transaction.");
    }

    // ---- the new table joins the solution as it is created ----

    // "`--solution` adds the new table to that solution as it's created."
    // Example: with `--solution TNCore`, a later `solution export TNCore` includes `tn_project`.
    [Fact]
    public async Task ApplyAsync_WithSolution_CreatesTheTableInThatSolution()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        var preview = await Preview(service);

        await service.ApplyAsync(EnvironmentUrl, "token", preview, "TNCore", CancellationToken.None);

        var call = Assert.Single(client.CreateEntityCalls);
        Expect.Equal("TNCore", call.SolutionUniqueName, "the table should be created with solution 'TNCore' so a later solution export includes it.");
    }

    // ---- a rejected create names the table ----

    // "If creating a table fails, later stages stop and say which table failed."
    // Example: Creation of `tn_project` rejected -> error names `tn_project`.
    [Fact]
    public async Task ApplyAsync_DataverseRejectsTheCreate_ThrowsAnErrorNamingTheTable()
    {
        var (service, client) = CreateService(tableExistsLive: false);
        client.CreateEntityFailure = new HttpRequestException("Dataverse request failed (400 BadRequest): schema name is already in use");
        var preview = await Preview(service);

        var ex = await Assert.ThrowsAsync<TableCreateFailedException>(() => service.ApplyAsync(EnvironmentUrl, "token", preview, "TNCore", CancellationToken.None));

        Expect.Equal("tn_project", ex.EntityLogicalName, "the failure should say which table could not be created.");
        Expect.Contains("tn_project", ex.Message, "the error message should name tn_project.");
        Expect.Contains("schema name is already in use", ex.Message, "the error message should keep Dataverse's own reason.");
    }
}

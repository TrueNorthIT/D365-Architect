using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// <see cref="ITableCreateService.ApplyAllAsync"/> - how <c>solution import</c> creates its new tables
/// (the "create tables in one transaction" behaviour, tagged with the "Feature"
/// trait "batch-table-create"), against a
/// <see cref="FakeDataverseClient"/>: every table that doesn't exist yet is one
/// <see cref="DataverseWrite.CreateEntity"/> in a single <c>ExecuteTransactionAsync</c> call (which
/// <see cref="DataverseClient"/> sends as one <c>$batch</c> changeset); a failure keeps none and is
/// reported against the table whose request Dataverse named by Content-ID.
/// </summary>
public sealed class TableCreateServiceBatchTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    private static EntityDefinition TableNamed(string logicalName, string schemaName)
    {
        var template = TableCreateFixtures.Table();
        return new EntityDefinition
        {
            LogicalName = logicalName,
            SchemaName = schemaName,
            DisplayName = template.DisplayName,
            PluralDisplayName = template.PluralDisplayName,
            Description = template.Description,
            OwnershipType = template.OwnershipType,
            PrimaryNameAttribute = template.PrimaryNameAttribute,
            Attributes = template.Attributes,
        };
    }

    /// <summary>Previews each table against a fake where only <paramref name="liveTables"/> exist.</summary>
    private static async Task<(TableCreateService Service, FakeDataverseClient Client, List<TableCreatePreview> Previews)> Build(string[] liveTables, params (string Logical, string Schema)[] tables)
    {
        var client = new FakeDataverseClient { EntityExists = name => liveTables.Contains(name) };
        var service = new TableCreateService(client);
        var previews = new List<TableCreatePreview>();
        foreach (var (logical, schema) in tables)
        {
            previews.Add(await service.PreviewAsync(EnvironmentUrl, "token", TableNamed(logical, schema), CancellationToken.None));
        }

        return (service, client, previews);
    }

    // "All new tables are created in one transaction (a single Web API $batch changeset of POST EntityDefinitions requests)."
    // Code path: TableCreateService.ApplyAllAsync -> IDataverseClient.ExecuteTransactionAsync([CreateEntity, CreateEntity]) once.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_TwoNewTables_AreOneTransactionOfTwoCreateEntityWritesInOrder()
    {
        var (service, client, previews) = await Build([], ("tn_project", "tn_Project"), ("tn_task", "tn_Task"));

        await service.ApplyAllAsync(EnvironmentUrl, "token", previews, "TNCore", CancellationToken.None);

        Expect.Equal(1, client.ExecuteTransactionCalls.Count, "all the creates should go in ONE ExecuteTransactionAsync call.");
        var writes = client.ExecuteTransactionCalls.FirstOrDefault() ?? [];
        Expect.Sequence(["tn_Project", "tn_Task"], writes.OfType<DataverseWrite.CreateEntity>().Select(w => (string?)w.Metadata["SchemaName"] ?? "").ToList(), "the transaction should be one CreateEntity per new table, in the order given.");
        Expect.Equal(2, writes.Count, "the transaction should hold nothing but the table creates (no columns, relationships or updates).");
    }

    // The body of each create is the one previewed (the primary name column travels inside it).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_EachWriteCarriesItsTablesPreviewedBody()
    {
        var (service, client, previews) = await Build([], ("tn_project", "tn_Project"), ("tn_task", "tn_Task"));

        await service.ApplyAllAsync(EnvironmentUrl, "token", previews, solutionUniqueName: null, CancellationToken.None);

        var writes = (client.ExecuteTransactionCalls.FirstOrDefault() ?? []).OfType<DataverseWrite.CreateEntity>().ToList();
        Expect.Equal(2, writes.Count, "both tables should be created.");
        Expect.True(ReferenceEquals(previews[0].RequestBody, writes.ElementAtOrDefault(0)?.Metadata) && ReferenceEquals(previews[1].RequestBody, writes.ElementAtOrDefault(1)?.Metadata),
            "each CreateEntity should carry the request body its table's preview built.");
    }

    // A table that already exists has no request body to send: only the missing ones are in the transaction.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_SkipsTablesThatAlreadyExist()
    {
        var (service, client, previews) = await Build(["tn_project"], ("tn_project", "tn_Project"), ("tn_task", "tn_Task"));

        await service.ApplyAllAsync(EnvironmentUrl, "token", previews, "TNCore", CancellationToken.None);

        var writes = (client.ExecuteTransactionCalls.FirstOrDefault() ?? []).OfType<DataverseWrite.CreateEntity>().ToList();
        Expect.Sequence(["tn_Task"], writes.Select(w => (string?)w.Metadata["SchemaName"] ?? "").ToList(), "only the table that doesn't exist should be created.");
    }

    // Nothing to create: nothing to send (an empty changeset is not a request).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_EveryTableAlreadyExists_SendsNothing()
    {
        var (service, client, previews) = await Build(["tn_project", "tn_task"], ("tn_project", "tn_Project"), ("tn_task", "tn_Task"));

        await service.ApplyAllAsync(EnvironmentUrl, "token", previews, "TNCore", CancellationToken.None);

        Expect.Empty(client.ExecuteTransactionCalls, "with nothing to create, no transaction should be sent.");
    }

    // "Tables created together are still added to `--solution`" - the solution name is on every create in the changeset.
    // Code path: DataverseWrite.CreateEntity(SolutionUniqueName) -> DataverseClient sends MSCRM.SolutionUniqueName per request (see DataverseClientBatchCreateTests).
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_WithSolution_EveryCreateIsInThatSolution()
    {
        var (service, client, previews) = await Build([], ("tn_project", "tn_Project"), ("tn_task", "tn_Task"));

        await service.ApplyAllAsync(EnvironmentUrl, "token", previews, "TNCore", CancellationToken.None);

        var solutions = (client.ExecuteTransactionCalls.FirstOrDefault() ?? []).OfType<DataverseWrite.CreateEntity>().Select(w => w.SolutionUniqueName).ToList();
        Expect.Equal(2, solutions.Count, "both tables should be created.");
        Expect.True(solutions.All(s => s == "TNCore"), $"'--solution TNCore' should reach every create in the transaction. Actual: <{string.Join(", ", solutions.Select(s => s ?? "null"))}>.");
    }

    // "the error names the table that failed (Dataverse's batch error identifies the failing request by Content-ID - map it back to the table)."
    // Here request 2 of 3 failed: tn_task. Nothing is kept.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_RolledBackNamingRequestTwo_ThrowsNamingTheSecondTable()
    {
        var (service, client, previews) = await Build([], ("tn_project", "tn_Project"), ("tn_task", "tn_Task"), ("tn_note", "tn_Note"));
        client.TransactionFailure = (2, "schema name tn_Task is already in use");

        var ex = await Assert.ThrowsAsync<TableCreateFailedException>(() => service.ApplyAllAsync(EnvironmentUrl, "token", previews, "TNCore", CancellationToken.None));

        Expect.Equal("tn_task", ex.EntityLogicalName, "Content-ID 2 is the second create, so the failure should name tn_task.");
        Expect.Contains("tn_task", ex.Message, "the message should name the table that failed.");
        Expect.Contains("already in use", ex.Message, "the message should keep Dataverse's own reason.");
        Expect.Empty(client.CreateEntityCalls, "a rolled-back transaction keeps no table.");
    }

    // Content-IDs count the requests actually sent. tn_project already exists, so it has no request: Content-ID 2 is the SECOND create sent (tn_note), not the second table given.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_ContentIdsCountOnlyTheRequestsSent()
    {
        var (service, client, previews) = await Build(["tn_project"], ("tn_project", "tn_Project"), ("tn_task", "tn_Task"), ("tn_note", "tn_Note"));
        client.TransactionFailure = (2, "schema name tn_Note is already in use");

        var ex = await Assert.ThrowsAsync<TableCreateFailedException>(() => service.ApplyAllAsync(EnvironmentUrl, "token", previews, "TNCore", CancellationToken.None));

        Expect.Equal("tn_note", ex.EntityLogicalName, "with tn_project skipped, requests are tn_task (1) and tn_note (2), so Content-ID 2 is tn_note.");
    }

    // A single table can be named even when Dataverse didn't say which request failed - there is only one it could be.
    [Fact]
    [Trait("Feature", "batch-table-create")]
    public async Task ApplyAllAsync_OnlyOneTableAndNoContentId_StillNamesThatTable()
    {
        var (service, client, previews) = await Build([], ("tn_project", "tn_Project"));
        client.TransactionFailure = (null, "schema name tn_Project is already in use");

        var ex = await Assert.ThrowsAsync<TableCreateFailedException>(() => service.ApplyAllAsync(EnvironmentUrl, "token", previews, "TNCore", CancellationToken.None));

        Expect.Equal("tn_project", ex.EntityLogicalName, "with one table in the transaction the failure can only be that table.");
    }
}

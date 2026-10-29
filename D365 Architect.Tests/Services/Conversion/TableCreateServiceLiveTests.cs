using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// <see cref="ITableCreateService.ApplyAllAsync"/> against a real environment - what
/// <see cref="TableCreateServiceBatchTests"/>' fake can't prove: that Dataverse really runs the <c>$batch</c> changeset as
/// one transaction, reports the failing part's Content-ID the way <see cref="ODataBatchResponseParser"/> reads it, and
/// honours <c>MSCRM.SolutionUniqueName</c> on each part. Live tests: skipped unless <see cref="LiveDataverse.Settings"/> is
/// set (see docs/DEVELOPING.md). Both tests are in one class so they run one after the other - concurrent metadata
/// writes to one environment fight over the customisation lock. Each table create takes 14-40 s, so each test runs
/// under <see cref="TestTimeout"/>; whatever a test created is deleted in <see cref="DisposeAsync"/>, pass or fail.
/// </summary>
[Trait("Category", "Live")]
public sealed class TableCreateServiceLiveTests : IAsyncLifetime
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(10);

    private readonly List<string> _tablesToDelete = [];
    private Guid? _solutionToDelete;
    private LiveDataverse? _live;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_live is null)
        {
            return;
        }

        // Try everything before reporting, so one failed delete doesn't leave the rest behind.
        var failures = new List<Exception>();
        foreach (var table in _tablesToDelete)
        {
            try
            {
                await _live.DeleteTableAsync(table, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (_solutionToDelete is { } solutionId)
        {
            try
            {
                await _live.DeleteSolutionAsync(solutionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        _live.Dispose();
        if (failures.Count > 0)
        {
            throw new AggregateException("Live test clean-up failed; delete the leftover <prefix>_lt* components by hand.", failures);
        }
    }

    // A failure on the 3rd create rolls back the 1st and 2nd, and is reported against the 3rd.
    //
    // The 3rd table is invalid because its schemaName repeats the 1st table's (a copy-pasted *.table.yml with only
    // `entity:` changed). Client-side validation can't stop that: TableCreateValidator checks each file on its own, and
    // PreviewAsync's existence check asks about the 3rd table's own logical name, which isn't live. Dataverse can only
    // reject it once the 1st create has run inside the same changeset - so the rejection is reliably server-side, comes
    // from the last part (not the first), and proves the earlier, successful parts are undone with it. A publisher
    // prefix the developer doesn't own was the alternative, but whether Dataverse checks the prefix with no solution
    // given isn't something to rely on.
    [LiveFact]
    public async Task ApplyAllAsync_ThirdTableRejected_NamesItAndKeepsNeitherOfTheOthers()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        _live = await LiveDataverse.ConnectAsync(ct);
        var (a, b, c) = (_live.NewName("a"), _live.NewName("b"), _live.NewName("c"));
        _tablesToDelete.AddRange([a, b, c]);

        var service = new TableCreateService(_live.Client);
        var previews = new List<TableCreatePreview>
        {
            await Preview(service, Table(a, schemaName: a), ct),
            await Preview(service, Table(b, schemaName: b), ct),
            await Preview(service, Table(c, schemaName: a), ct),
        };

        var ex = await Assert.ThrowsAsync<TableCreateFailedException>(() =>
            service.ApplyAllAsync(_live.EnvironmentUrl, _live.AccessToken, previews, solutionUniqueName: null, ct));

        Expect.Equal(c, ex.EntityLogicalName, $"The failure should name the 3rd table (Dataverse said: {ex.InnerException?.Message}).");
        Expect.True(!await _live.Client.EntityExistsAsync(_live.EnvironmentUrl, _live.AccessToken, a, ct), $"The 1st table '{a}' should have been rolled back.");
        Expect.True(!await _live.Client.EntityExistsAsync(_live.EnvironmentUrl, _live.AccessToken, b, ct), $"The 2nd table '{b}' should have been rolled back.");
    }

    // Two creates commit together, and each table joins the solution named on its part.
    [LiveFact]
    public async Task ApplyAllAsync_WithSolution_CreatesBothTablesAsComponentsOfIt()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        _live = await LiveDataverse.ConnectAsync(ct);
        var solution = _live.NewName();
        _solutionToDelete = await _live.CreateSolutionAsync(solution, ct);
        var (a, b) = (_live.NewName("a"), _live.NewName("b"));
        _tablesToDelete.AddRange([a, b]);

        var service = new TableCreateService(_live.Client);
        var previews = new List<TableCreatePreview>
        {
            await Preview(service, Table(a, schemaName: a), ct),
            await Preview(service, Table(b, schemaName: b), ct),
        };

        await service.ApplyAllAsync(_live.EnvironmentUrl, _live.AccessToken, previews, solution, ct);

        var components = await _live.GetSolutionComponentIdsAsync(_solutionToDelete.Value, componentType: 1, ct);
        foreach (var table in new[] { a, b })
        {
            var metadataId = await _live.TryGetEntityMetadataIdAsync(table, ct);
            Expect.True(metadataId is not null, $"Table '{table}' should exist.");
            Expect.True(components.Contains(metadataId!.Value), $"Table '{table}' ({metadataId}) should be a component of solution '{solution}'. Components: {string.Join(", ", components)}.");
        }
    }

    private async Task<TableCreatePreview> Preview(TableCreateService service, EntityDefinition table, CancellationToken ct)
    {
        var preview = await service.PreviewAsync(_live!.EnvironmentUrl, _live.AccessToken, table, ct);
        Expect.True(preview.HasChanges, $"'{table.LogicalName}' should not exist before the test creates it.");
        return preview;
    }

    private EntityDefinition Table(string logicalName, string schemaName) => new()
    {
        LogicalName = logicalName,
        SchemaName = schemaName,
        DisplayName = $"Live test {logicalName}",
        PluralDisplayName = $"Live tests {logicalName}",
        OwnershipType = "UserOwned",
        PrimaryNameAttribute = $"{_live!.Prefix}_name",
        Attributes = [new AttributeDefinition { Name = $"{_live.Prefix}_name", Type = "String", SchemaName = $"{_live.Prefix}_name", DisplayName = "Name" }],
    };
}

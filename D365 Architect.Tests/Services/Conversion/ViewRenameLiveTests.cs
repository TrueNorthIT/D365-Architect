using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// What the fake-backed rename tests can't prove: that Dataverse accepts <c>name</c> on a <c>savedquery</c> update, keeps the view's
/// id, and that importing the same file again then reports no changes. Creates a uniquely named public view on <c>account</c>
/// (a standard table, so the test needs no table of its own), renames it through <see cref="ViewImportService"/>, and deletes the view
/// in <see cref="DisposeAsync"/> by either of its names, pass or fail. Live test: skipped unless <see cref="LiveDataverse.Settings"/>
/// is set (see docs/DEVELOPING.md). Use your own throwaway environment only: it publishes <c>account</c>.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveCollection.Name)]
public sealed class ViewRenameLiveTests : IAsyncLifetime
{
    private const string Table = "account";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(10);

    private readonly List<string> _viewNamesToDelete = [];
    private LiveDataverse? _live;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_live is null)
        {
            return;
        }

        try
        {
            await _live.DeleteViewsNamedAsync(Table, _viewNamesToDelete, CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new AggregateException("Live test clean-up failed; delete the leftover <prefix>_lt* views on account by hand.", ex);
        }
        finally
        {
            _live.Dispose();
        }
    }

    // REQ-1, REQ-3, REQ-8: a file with the view's savedQueryId and a new name renames the live view (same id), and a second import is quiet.
    [LiveFact]
    public async Task ImportedRename_ChangesTheLiveNameKeepsTheIdAndReimportsAsNoChanges()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        _live = await LiveDataverse.ConnectAsync(ct);
        var original = _live.NewName("view");
        var renamed = _live.NewName("renamed");
        _viewNamesToDelete.AddRange([original, renamed]);
        var service = new ViewImportService(_live.Client);

        // A hand-written public view: no savedQueryId yet, so it is created, then published so a GET sees it.
        var created = await service.PreviewAsync(_live.EnvironmentUrl, _live.AccessToken, new ViewDefinition { Name = original, Entity = Table, Columns = [new ViewColumn { Name = "name" }] }, ct);
        Expect.True(created.IsCreate, $"'{original}' should not exist before the test creates it.");
        await service.ApplyAsync(_live.EnvironmentUrl, _live.AccessToken, created, ct);
        await service.PublishAsync(_live.EnvironmentUrl, _live.AccessToken, created, ct);
        var live = await _live.Client.TryGetSavedQueryAsync(_live.EnvironmentUrl, _live.AccessToken, Table, original, ct);
        Expect.True(live is not null, $"The created view '{original}' should be live.");
        var viewId = live!.SavedQueryId;

        // The rename: same id, new name, nothing else in the file.
        var renameFile = new ViewDefinition { Name = renamed, Entity = Table, SavedQueryId = viewId };
        var rename = await service.PreviewAsync(_live.EnvironmentUrl, _live.AccessToken, renameFile, ct);
        Expect.True(rename.HasChanges, $"Renaming '{original}' to '{renamed}' should be previewed as a change.");
        await service.ApplyAsync(_live.EnvironmentUrl, _live.AccessToken, rename, ct);
        await service.PublishAsync(_live.EnvironmentUrl, _live.AccessToken, rename, ct);

        var after = await _live.Client.TryGetSavedQueryByIdAsync(_live.EnvironmentUrl, _live.AccessToken, viewId, ct);
        Expect.Equal(renamed, after?.Name, $"View {viewId} should now be named '{renamed}' (Dataverse accepts name on a savedquery update).");

        var again = await service.PreviewAsync(_live.EnvironmentUrl, _live.AccessToken, renameFile, ct);
        Expect.True(!again.HasChanges, "Importing the same file again should show no changes.");
    }
}

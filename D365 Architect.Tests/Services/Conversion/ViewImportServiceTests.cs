using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="ViewImportService"/>, in particular that a null
/// <c>Description</c>/<c>FetchXml</c>/<c>LayoutXml</c> in the local YAML
/// really does mean "don't touch this" rather than "clear it" - the same
/// convention checked for tables/choices, worth its own direct test here
/// since a view is the one place this session specifically chased down
/// whether the underlying PATCH call (<see cref="IDataverseClient.UpdateSavedQueryAsync"/>)
/// honors it (it does - see that method's own doc comment).
/// </summary>
public sealed class ViewImportServiceTests
{
    private static ViewDefinition View(string? description = null, string? fetchXml = null, string? layoutXml = null) => new()
    {
        Name = "Test View",
        Entity = "tn_test",
        Description = description,
        FetchXml = fetchXml,
        LayoutXml = layoutXml,
    };

    [Fact]
    public async Task PreviewAsync_NullLocalFields_ProduceNoChanges()
    {
        var client = new FakeDataverseClient { SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), "Existing description", "<fetch/>", "<grid/>") };
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", View(), CancellationToken.None);

        Assert.False(preview.HasChanges);
    }

    [Fact]
    public async Task ApplyAsync_NullDescription_NeverClearsTheLiveDescription()
    {
        var client = new FakeDataverseClient { SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), "Existing description", null, null) };
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", View(fetchXml: "<fetch>new</fetch>"), CancellationToken.None);
        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        var call = Assert.Single(client.UpdateSavedQueryCalls);
        Assert.Null(call.Description);
        Assert.Equal("<fetch>new</fetch>", call.FetchXml);
    }

    [Fact]
    public async Task PreviewAsync_ChangedFetchXml_HasChangesTrue()
    {
        var client = new FakeDataverseClient { SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), null, "<fetch>old</fetch>", null) };
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", View(fetchXml: "<fetch>new</fetch>"), CancellationToken.None);

        Assert.True(preview.HasChanges);
    }

    // ---- Issue #35 (sub-issue of #30): pinning a view by savedQueryId ----

    private static readonly Uri Environment = new("https://test.crm.dynamics.com");
    private static readonly Guid TargetViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid SiblingViewId = new("aaaaaaaa-0000-0000-0000-000000000001");

    private static ViewDefinition ViewPinnedTo(Guid savedQueryId, string? fetchXml = null) => new()
    {
        Name = "My Inspections",
        Entity = "tn_inspection",
        SavedQueryId = savedQueryId,
        FetchXml = fetchXml,
    };

    // Given two live views share the name 'My Inspections' on tn_inspection
    // (a by-name lookup is ambiguous) and the YAML carries the savedQueryId
    // of one of them, when ViewImportService.PreviewAsync runs, then it
    // resolves that view instead of refusing.
    [Fact]
    public async Task PreviewAsync_WithSavedQueryId_ResolvesThatViewEvenWhenTheNameIsAmbiguous()
    {
        var client = new FakeDataverseClient { SavedQueryNameIsAmbiguous = true };
        client.SavedQueriesById[TargetViewId] = new ExistingSavedQuery(TargetViewId, null, "<fetch>old</fetch>", null);
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", ViewPinnedTo(TargetViewId, "<fetch>new</fetch>"), CancellationToken.None);

        Assert.True(preview.SavedQueryId == TargetViewId, $"expected the preview to target the view named by the YAML's savedQueryId ({TargetViewId}), but it targeted {preview.SavedQueryId}.");
        Assert.True(preview.HasChanges, "the pinned view's live FetchXML differs from the YAML's, so a change should be previewed.");
    }

    // The id, not the name, picks the target. Given the by-name
    // lookup would find a DIFFERENT view (a sibling that shares the name)
    // while the YAML pins the target's id, then the preview is for the
    // target, not the sibling.
    [Fact]
    public async Task PreviewAsync_WithSavedQueryId_PrefersTheIdOverWhateverTheNameWouldMatch()
    {
        var client = new FakeDataverseClient { SavedQuery = new ExistingSavedQuery(SiblingViewId, null, "<fetch>sibling</fetch>", null) };
        client.SavedQueriesById[TargetViewId] = new ExistingSavedQuery(TargetViewId, null, "<fetch>target</fetch>", null);
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", ViewPinnedTo(TargetViewId, "<fetch>new</fetch>"), CancellationToken.None);

        Assert.True(preview.SavedQueryId == TargetViewId, $"expected the preview to target the view named by the YAML's savedQueryId ({TargetViewId}), but it targeted {preview.SavedQueryId}.");
        Assert.Equal("<fetch>target</fetch>", preview.ExistingFetchXml);
    }

    // Applying the preview writes to the pinned view and no other.
    [Fact]
    public async Task ApplyAsync_WithSavedQueryId_UpdatesTheViewTheIdNames()
    {
        var client = new FakeDataverseClient { SavedQueryNameIsAmbiguous = true };
        client.SavedQueriesById[TargetViewId] = new ExistingSavedQuery(TargetViewId, null, "<fetch>old</fetch>", null);
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", ViewPinnedTo(TargetViewId, "<fetch>new</fetch>"), CancellationToken.None);
        await service.ApplyAsync(Environment, "token", preview, CancellationToken.None);

        var call = Assert.Single(client.UpdateSavedQueryCalls);
        Assert.True(call.SavedQueryId == TargetViewId, $"expected the update to go to {TargetViewId}, but it went to {call.SavedQueryId}.");
        Assert.Equal("<fetch>new</fetch>", call.FetchXml);
    }

    // A stale id (nothing live has it) is an error, never a quiet fall back to the name.
    [Fact]
    public async Task PreviewAsync_WithSavedQueryIdNotFoundLive_ThrowsViewNotFoundException()
    {
        var client = new FakeDataverseClient { SavedQuery = new ExistingSavedQuery(SiblingViewId, null, "<fetch/>", null) };
        var service = new ViewImportService(client);

        var ex = await Assert.ThrowsAsync<ViewNotFoundException>(() =>
            service.PreviewAsync(Environment, "token", ViewPinnedTo(TargetViewId, "<fetch>new</fetch>"), CancellationToken.None));

        Assert.Equal(TargetViewId, ex.SavedQueryId);
    }

    // The id resolves to a view on a different table: refuse, naming both.
    [Fact]
    public async Task PreviewAsync_WithSavedQueryIdOnADifferentEntity_RefusesNamingBoth()
    {
        var client = new FakeDataverseClient();
        client.SavedQueriesById[TargetViewId] = new ExistingSavedQuery(TargetViewId, null, "<fetch/>", null, "account", "Active Accounts");
        var service = new ViewImportService(client);

        var ex = await Assert.ThrowsAsync<ViewIdentityMismatchException>(() =>
            service.PreviewAsync(Environment, "token", ViewPinnedTo(TargetViewId, "<fetch>new</fetch>"), CancellationToken.None));

        Assert.Contains("'account'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'tn_inspection'", ex.Message, StringComparison.Ordinal);
    }

    // Same table, different name: a legitimate rename, so warn but proceed.
    [Fact]
    public async Task PreviewAsync_WithSavedQueryIdWhoseLiveNameDiffers_WarnsButProceeds()
    {
        var client = new FakeDataverseClient();
        client.SavedQueriesById[TargetViewId] = new ExistingSavedQuery(TargetViewId, null, "<fetch>old</fetch>", null, "tn_inspection", "Old Name");
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", ViewPinnedTo(TargetViewId, "<fetch>new</fetch>"), CancellationToken.None);

        Assert.True(preview.HasChanges);
        Assert.NotNull(preview.IdentityMismatchWarning);
        Assert.Contains("'Old Name'", preview.IdentityMismatchWarning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsync_WithSavedQueryIdAndMatchingIdentity_HasNoWarning()
    {
        var client = new FakeDataverseClient();
        client.SavedQueriesById[TargetViewId] = new ExistingSavedQuery(TargetViewId, null, "<fetch>old</fetch>", null, "TN_Inspection", "My Inspections");
        var service = new ViewImportService(client);

        var preview = await service.PreviewAsync(Environment, "token", ViewPinnedTo(TargetViewId, "<fetch>new</fetch>"), CancellationToken.None);

        Assert.Null(preview.IdentityMismatchWarning);
    }

    // Without a savedQueryId, an ambiguous name still refuses. Given two live
    // views sharing the name, PreviewAsync throws AmbiguousSavedQueryException
    // rather than guessing. (The message content is covered in
    // DataverseClientAmbiguousSavedQueryTests.)
    [Fact]
    public async Task PreviewAsync_WithoutSavedQueryIdAndAnAmbiguousName_StillRefuses()
    {
        var client = new FakeDataverseClient { SavedQueryNameIsAmbiguous = true };
        var service = new ViewImportService(client);

        await Assert.ThrowsAsync<AmbiguousSavedQueryException>(() =>
            service.PreviewAsync(Environment, "token", View(fetchXml: "<fetch>new</fetch>"), CancellationToken.None));
    }
}

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
}

using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// `solution import` previews every asset before applying any, so a column
/// bound to a global choice that choices.yml creates in the same run can't see
/// that choice live at preview time. The table preview is therefore told which
/// choices are pending.
/// </summary>
public sealed class TableImportServicePendingGlobalChoiceTests
{
    private static readonly Uri Environment = new("https://test.crm.dynamics.com");
    private static readonly Guid ChoiceMetadataId = new("aaaaaaaa-1111-2222-3333-444444444444");

    private static (TableImportService Service, FakeDataverseClient Client) CreateService()
    {
        var client = new FakeDataverseClient
        {
            EntityDefinitionJson = """{ "LogicalName": "tn_test", "SchemaName": "tn_Test", "OwnershipType": "UserOwned", "Attributes": [] }""",
            GlobalOptionSetJsonByName = _ => null,
        };
        return (new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)), client);
    }

    private static EntityDefinition LocalWithChoiceColumn() => new()
    {
        LogicalName = "tn_test",
        Attributes = [new AttributeDefinitionBuilder("Picklist", "tn_Colour") { Name = "tn_colour", GlobalOptionSetName = "tn_colours" }.Build()],
    };

    [Fact]
    public async Task PreviewAsync_ChoiceIsCreatedByTheSameRun_PlansTheColumnAsACreate()
    {
        var (service, _) = CreateService();

        var preview = await service.PreviewAsync(Environment, "token", LocalWithChoiceColumn(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TN_Colours" }, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Create, plan.Action);
        Assert.Equal("tn_colours", plan.PendingGlobalChoiceName);
        Assert.True(preview.HasChanges);
    }

    [Fact]
    public async Task PreviewAsync_ChoiceIsNeitherLiveNorPending_StillPlansTheColumnAsInvalid()
    {
        var (service, _) = CreateService();

        var preview = await service.PreviewAsync(Environment, "token", LocalWithChoiceColumn(), new HashSet<string>(), CancellationToken.None);
        var viaPlainOverload = await service.PreviewAsync(Environment, "token", LocalWithChoiceColumn(), CancellationToken.None);

        Assert.Equal(AttributeImportAction.Invalid, Assert.Single(preview.AttributePlans).Action);
        Assert.Equal(AttributeImportAction.Invalid, Assert.Single(viaPlainOverload.AttributePlans).Action);
    }

    [Fact]
    public async Task ApplyAsync_PendingChoiceNowExists_BindsTheColumnToItsRealMetadataId()
    {
        var (service, client) = CreateService();
        var preview = await service.PreviewAsync(Environment, "token", LocalWithChoiceColumn(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tn_colours" }, CancellationToken.None);

        // choices.yml has been applied by now.
        client.GlobalOptionSetJsonByName = _ => $$"""{ "MetadataId": "{{ChoiceMetadataId}}" }""";
        await service.ApplyAsync(Environment, "token", preview, CancellationToken.None);

        var write = Assert.Single(Assert.Single(client.ExecuteTransactionCalls));
        var create = Assert.IsType<DataverseWrite.CreateAttribute>(write);
        Assert.Equal($"/GlobalOptionSetDefinitions({ChoiceMetadataId:D})", (string?)create.Metadata["GlobalOptionSet@odata.bind"]);
    }

    [Fact]
    public async Task ApplyAsync_PendingChoiceWasNeverCreated_FailsLoudlyWithoutWriting()
    {
        var (service, client) = CreateService();
        var preview = await service.PreviewAsync(Environment, "token", LocalWithChoiceColumn(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tn_colours" }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(Environment, "token", preview, CancellationToken.None));

        Assert.Contains("tn_colours", ex.Message, StringComparison.Ordinal);
        Assert.Empty(client.ExecuteTransactionCalls);
    }
}

using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="GlobalChoiceImportService"/> against a
/// <see cref="FakeDataverseClient"/>, including the duplicate-<c>Name</c>
/// guard found and fixed this session (round 2), mirroring
/// <see cref="TableImportServiceTests"/>'s own duplicate-name guard coverage.
/// </summary>
public sealed class GlobalChoiceImportServiceTests
{
    private static GlobalChoiceDefinition Choice(string name, string? displayName = null, string? description = null, IReadOnlyList<AttributeOptionDefinition>? options = null) => new()
    {
        Name = name,
        DisplayName = displayName,
        Description = description,
        Options = options,
    };

    [Fact]
    public async Task PreviewAsync_TwoEntriesWithSameName_BothPlanAsInvalid_WithoutAnyLiveLookup()
    {
        var client = new FakeDataverseClient
        {
            // Deliberately not configured: a lookup attempt for a duplicate
            // name should never even happen.
            GlobalOptionSetJsonByName = _ => throw new InvalidOperationException("Should never look up a duplicate name live."),
        };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_dup", options: [new AttributeOptionDefinition { Value = 1, Label = "A" }]), Choice("tn_dup", options: [new AttributeOptionDefinition { Value = 2, Label = "B" }]) };

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        Assert.All(preview.ChoicePlans, p => Assert.Equal(GlobalChoiceImportAction.Invalid, p.Action));
        Assert.All(preview.ChoicePlans, p => Assert.Contains("unique", p.Reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreviewAsync_DuplicateNameIsCaseInsensitive()
    {
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => throw new InvalidOperationException("no lookup expected") };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_Dup", options: [new AttributeOptionDefinition { Value = 1, Label = "A" }]), Choice("TN_DUP", options: [new AttributeOptionDefinition { Value = 2, Label = "B" }]) };

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        Assert.All(preview.ChoicePlans, p => Assert.Equal(GlobalChoiceImportAction.Invalid, p.Action));
    }

    [Fact]
    public async Task PreviewAsync_NewChoice_PlansCreate()
    {
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => null };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_new", options: [new AttributeOptionDefinition { Value = 1, Label = "A" }]) };

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        var plan = Assert.Single(preview.ChoicePlans);
        Assert.Equal(GlobalChoiceImportAction.Create, plan.Action);
    }

    [Fact]
    public async Task PreviewAsync_NewChoiceFailingValidation_PlansInvalidWithoutBuildingARequest()
    {
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => null };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_new", options: null) }; // no options - required to create

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        var plan = Assert.Single(preview.ChoicePlans);
        Assert.Equal(GlobalChoiceImportAction.Invalid, plan.Action);
        Assert.Null(plan.RequestBody);
    }

    [Fact]
    public async Task PreviewAsync_UnchangedChoice_PlansUnchanged()
    {
        var existingJson = """{ "MetadataId": "11111111-1111-1111-1111-111111111111", "Name": "tn_existing", "DisplayName": { "UserLocalizedLabel": { "Label": "Existing" } }, "Options": [ { "Value": 1, "Label": { "UserLocalizedLabel": { "Label": "A" } } } ] }""";
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => existingJson };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_existing", displayName: "Existing", options: [new AttributeOptionDefinition { Value = 1, Label = "A" }]) };

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        var plan = Assert.Single(preview.ChoicePlans);
        Assert.Equal(GlobalChoiceImportAction.Unchanged, plan.Action);
        Assert.False(preview.HasChanges);
    }

    [Fact]
    public async Task PreviewAsync_ChangedDescription_PlansUpdateWithRequestBody()
    {
        var existingJson = """{ "MetadataId": "11111111-1111-1111-1111-111111111111", "Name": "tn_existing", "Options": [] }""";
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => existingJson };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_existing", description: "New description") };

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        var plan = Assert.Single(preview.ChoicePlans);
        Assert.Equal(GlobalChoiceImportAction.Update, plan.Action);
        Assert.NotNull(plan.RequestBody);
        Assert.True(preview.HasChanges);
    }

    [Fact]
    public async Task PreviewAsync_OnlyOptionsChanged_PlansUpdateWithNullRequestBodyButSetOptionChanges()
    {
        var existingJson = """{ "MetadataId": "11111111-1111-1111-1111-111111111111", "Name": "tn_existing", "Options": [ { "Value": 1, "Label": { "UserLocalizedLabel": { "Label": "Red" } } } ] }""";
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => existingJson };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_existing", options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }, new AttributeOptionDefinition { Value = 2, Label = "Blue" }]) };

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        var plan = Assert.Single(preview.ChoicePlans);
        Assert.Equal(GlobalChoiceImportAction.Update, plan.Action);
        Assert.Null(plan.RequestBody); // no base-field change, so no PUT body needed
        Assert.NotNull(plan.OptionChanges);
    }

    [Fact]
    public async Task ApplyAsync_CreatePlan_CallsCreateGlobalOptionSet()
    {
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => null };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_new", options: [new AttributeOptionDefinition { Value = 1, Label = "A" }]) };
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        Assert.Single(client.CreateGlobalOptionSetCalls);
    }

    [Fact]
    public async Task ApplyAsync_FiveArgOverload_WithSolutionUniqueName_PassesItToCreateGlobalOptionSetAsync()
    {
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => null };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_new", options: [new AttributeOptionDefinition { Value = 1, Label = "A" }]) };
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, solutionUniqueName: "JCTest", CancellationToken.None);

        var call = Assert.Single(client.CreateGlobalOptionSetCalls);
        Assert.Equal("JCTest", call.SolutionUniqueName);
    }

    [Fact]
    public async Task ApplyAsync_DefaultFourArgOverload_NeverAttachesASolutionUniqueName()
    {
        // Regression guard for the old default: a plain `choice import`
        // (no --solution) must still leave a new choice wherever
        // Dataverse's own default context puts it.
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => null };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_new", options: [new AttributeOptionDefinition { Value = 1, Label = "A" }]) };
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        var call = Assert.Single(client.CreateGlobalOptionSetCalls);
        Assert.Null(call.SolutionUniqueName);
    }

    [Fact]
    public async Task ApplyAsync_UpdatePlanWithBaseFieldChange_CallsUpdateGlobalOptionSetWithMetadataId()
    {
        var metadataId = Guid.NewGuid();
        var existingJson = $$"""{ "MetadataId": "{{metadataId}}", "Name": "tn_existing", "Options": [] }""";
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => existingJson };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_existing", description: "New description") };
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        var call = Assert.Single(client.UpdateGlobalOptionSetCalls);
        Assert.Equal(metadataId, call.MetadataId);
    }

    [Fact]
    public async Task ApplyAsync_OptionsOnlyUpdatePlan_NeverCallsUpdateGlobalOptionSet_OnlyOptionActions()
    {
        var existingJson = """{ "MetadataId": "11111111-1111-1111-1111-111111111111", "Name": "tn_existing", "Options": [] }""";
        var client = new FakeDataverseClient { GlobalOptionSetJsonByName = _ => existingJson };
        var service = new GlobalChoiceImportService(client);
        var locals = new List<GlobalChoiceDefinition> { Choice("tn_existing", options: [new AttributeOptionDefinition { Value = 1, Label = "New" }]) };
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", locals, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        Assert.Empty(client.UpdateGlobalOptionSetCalls);
        Assert.Single(client.InsertOptionValueCalls);
    }
}

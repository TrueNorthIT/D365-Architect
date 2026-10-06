using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="TableImportService"/>'s orchestration logic against a
/// <see cref="FakeDataverseClient"/> — no real HTTP call ever happens.
/// Includes the two duplicate-name guards found and fixed this session
/// (round 1): two new columns can't share a <c>SchemaName</c>, and two new
/// Lookup columns can't share a <c>RelationshipSchemaName</c>.
/// </summary>
public sealed class TableImportServiceTests
{
    private static (TableImportService Service, FakeDataverseClient Client) CreateService(string existingEntityJson)
    {
        var client = new FakeDataverseClient { EntityDefinitionJson = existingEntityJson };
        var reader = new EntityJsonDefinitionReader();
        var optionSetFetcher = new AttributeOptionSetFetcher(client);
        return (new TableImportService(client, reader, optionSetFetcher), client);
    }

    private static string Entity(string attributesJson) => $$"""
        {
          "LogicalName": "tn_test",
          "SchemaName": "tn_Test",
          "OwnershipType": "UserOwned",
          "Attributes": [ {{attributesJson}} ]
        }
        """;

    private static EntityDefinition Local(params AttributeDefinition[] attributes) => new()
    {
        LogicalName = "tn_test",
        Attributes = attributes,
    };

    private static AttributeDefinition Attr(string name, string type, string? schemaName = null, Action<AttributeDefinitionBuilder>? configure = null)
    {
        var builder = new AttributeDefinitionBuilder(type, schemaName ?? "tn_" + name) { Name = name };
        configure?.Invoke(builder);
        return builder.Build();
    }

    // ---- Duplicate SchemaName guard (round 1 regression) ----

    [Fact]
    public async Task PreviewAsync_TwoNewColumnsWithSameSchemaName_BothPlanAsInvalid()
    {
        var (service, _) = CreateService(Entity(""));
        var local = Local(
            Attr("tn_a", "String", schemaName: "tn_Dup"),
            Attr("tn_b", "String", schemaName: "tn_Dup"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        Assert.All(preview.AttributePlans.Where(p => p.LogicalName is "tn_a" or "tn_b"),
            plan => Assert.Equal(AttributeImportAction.Invalid, plan.Action));
    }

    [Fact]
    public async Task PreviewAsync_TwoNewLookupsWithSameRelationshipSchemaName_BothPlanAsInvalid()
    {
        var (service, _) = CreateService(Entity(""));
        var local = Local(
            Attr("tn_lookup1", "Lookup", schemaName: "tn_Lookup1", configure: b => { b.RelationshipSchemaName = "tn_test_dup"; b.Targets = ["contact"]; }),
            Attr("tn_lookup2", "Lookup", schemaName: "tn_Lookup2", configure: b => { b.RelationshipSchemaName = "tn_test_dup"; b.Targets = ["account"]; }));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plans = preview.AttributePlans.Where(p => p.LogicalName is "tn_lookup1" or "tn_lookup2").ToList();
        Assert.All(plans, p => Assert.Equal(AttributeImportAction.Invalid, p.Action));
        Assert.All(plans, p => Assert.Contains("RelationshipSchemaName", p.Reason));
    }

    [Fact]
    public async Task PreviewAsync_TwoNewLookupsWithDifferentRelationshipSchemaNames_BothCreate()
    {
        var (service, _) = CreateService(Entity(""));
        var local = Local(
            Attr("tn_lookup1", "Lookup", schemaName: "tn_Lookup1", configure: b => { b.RelationshipSchemaName = "tn_test_one"; b.Targets = ["contact"]; }),
            Attr("tn_lookup2", "Lookup", schemaName: "tn_Lookup2", configure: b => { b.RelationshipSchemaName = "tn_test_two"; b.Targets = ["account"]; }));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        Assert.All(preview.AttributePlans.Where(p => p.LogicalName is "tn_lookup1" or "tn_lookup2"),
            plan => Assert.Equal(AttributeImportAction.CreateLookupRelationship, plan.Action));
    }

    // ---- Create/Update/Unchanged/WouldRemove classification ----

    [Fact]
    public async Task PreviewAsync_NewColumn_PlansCreate()
    {
        var (service, _) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Create, plan.Action);
        Assert.NotNull(plan.RequestBody);
    }

    [Fact]
    public async Task PreviewAsync_UnchangedColumn_PlansUnchanged()
    {
        var (service, _) = CreateService(Entity("""{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing", "AttributeType": "String" }"""));
        var local = Local(Attr("tn_existing", "String", schemaName: "tn_Existing"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Unchanged, plan.Action);
    }

    [Fact]
    public async Task PreviewAsync_ChangedDisplayName_PlansUpdate()
    {
        var (service, client) = CreateService(Entity("""{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing", "AttributeType": "String" }"""));
        client.AttributeMetadataJson = (_, _) => """{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing" }""";
        var local = Local(Attr("tn_existing", "String", schemaName: "tn_Existing", configure: b => b.DisplayName = "New Display Name"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Update, plan.Action);
        Assert.NotNull(plan.RequestBody);
    }

    [Fact]
    public async Task PreviewAsync_ColumnLiveButMissingFromLocal_PlansWouldRemove()
    {
        var (service, _) = CreateService(Entity("""{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing", "AttributeType": "String" }"""));
        var local = Local(); // no attributes at all

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.WouldRemove, plan.Action);
    }

    [Fact]
    public async Task PreviewAsync_TypeMismatchOnExistingColumn_PlansInvalid()
    {
        var (service, _) = CreateService(Entity("""{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing", "AttributeType": "String" }"""));
        var local = Local(Attr("tn_existing", "Integer", schemaName: "tn_Existing"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Invalid, plan.Action);
        Assert.Contains("type", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewAsync_SchemaNameMismatchOnExistingColumn_PlansInvalid()
    {
        var (service, _) = CreateService(Entity("""{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing", "AttributeType": "String" }"""));
        var local = Local(Attr("tn_existing", "String", schemaName: "tn_Different"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Invalid, plan.Action);
    }

    [Fact]
    public async Task PreviewAsync_ChangedTargetsOnExistingLookup_PlansInvalid()
    {
        var existing = Entity("""{ "LogicalName": "tn_lookup", "SchemaName": "tn_Lookup", "AttributeType": "Lookup", "Targets": ["contact"] }""");
        var (service, _) = CreateService(existing);
        var local = Local(Attr("tn_lookup", "Lookup", schemaName: "tn_Lookup", configure: b => b.Targets = ["account"]));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Invalid, plan.Action);
        Assert.Contains("Targets", plan.Reason);
    }

    [Fact]
    public async Task PreviewAsync_NewOwnerStateOrStatusColumn_PlansInvalid()
    {
        var (service, _) = CreateService(Entity(""));
        var local = Local(Attr("tn_state", "State", schemaName: "tn_State"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.Invalid, plan.Action);
    }

    [Fact]
    public async Task PreviewAsync_NewUnsupportedType_PlansSkippedUnsupportedType()
    {
        var (service, _) = CreateService(Entity(""));
        var local = Local(Attr("tn_weird", "Double", schemaName: "tn_Weird"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var plan = Assert.Single(preview.AttributePlans);
        Assert.Equal(AttributeImportAction.SkippedUnsupportedType, plan.Action);
    }

    // ---- Diff display order (reordered-but-identical columns shouldn't show as noise) ----

    [Fact]
    public async Task PreviewAsync_ColumnsInDifferentOrderButOtherwiseIdentical_ExistingYamlLinesUpWithLocal_NoDiffNoise()
    {
        // Live/re-exported order (tn_b, tn_a) deliberately doesn't match the
        // order the local file lists them in (tn_a, tn_b) — exactly what
        // happens for real, every time, since Dataverse returns columns in
        // its own order regardless of how a human wrote the YAML. Without
        // EntityDefinition.WithAttributes/OrderForDiff re-sorting the
        // existing side to match, a plain positional line diff of the two
        // YAML blobs would show both columns as wholesale removed-and-re-added
        // even though nothing about either one actually changed.
        var existing = Entity("""
            { "LogicalName": "tn_b", "SchemaName": "tn_B", "AttributeType": "String" },
            { "LogicalName": "tn_a", "SchemaName": "tn_A", "AttributeType": "String" }
            """);
        var (service, _) = CreateService(existing);
        // SchemaName/OwnershipType set to match Entity()'s own values —
        // otherwise the diff also shows those as "removed" simply because
        // the local YAML never restates them, which is real (if separately
        // noisy) and not what this test is isolating.
        var local = new EntityDefinition
        {
            LogicalName = "tn_test",
            SchemaName = "tn_Test",
            OwnershipType = "UserOwned",
            Attributes = [Attr("tn_a", "String", schemaName: "tn_A"), Attr("tn_b", "String", schemaName: "tn_B")],
        };

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        Assert.All(preview.AttributePlans, plan => Assert.Equal(AttributeImportAction.Unchanged, plan.Action));

        var diff = TextDiff.Compute(preview.ExistingYaml, preview.NewYaml);
        Assert.DoesNotContain(diff, line => line.Kind != TextDiffLineKind.Unchanged);
    }

    [Fact]
    public async Task PreviewAsync_ColumnsInDifferentOrderWithOneLiveOnlyColumn_LiveOnlyColumnKeepsItsOwnPosition()
    {
        // tn_extra is live-only (WouldRemove) — OrderForDiff has no local
        // position to sort it by, so it should fall in after every matched
        // column rather than disappearing or reordering unpredictably.
        var existing = Entity("""
            { "LogicalName": "tn_b", "SchemaName": "tn_B", "AttributeType": "String" },
            { "LogicalName": "tn_extra", "SchemaName": "tn_Extra", "AttributeType": "String" },
            { "LogicalName": "tn_a", "SchemaName": "tn_A", "AttributeType": "String" }
            """);
        var (service, _) = CreateService(existing);
        var local = Local(
            Attr("tn_a", "String", schemaName: "tn_A"),
            Attr("tn_b", "String", schemaName: "tn_B"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        var aIndex = preview.ExistingYaml.IndexOf("tn_a", StringComparison.Ordinal);
        var bIndex = preview.ExistingYaml.IndexOf("tn_b", StringComparison.Ordinal);
        var extraIndex = preview.ExistingYaml.IndexOf("tn_extra", StringComparison.Ordinal);
        Assert.True(aIndex < bIndex, "tn_a (local position 0) should come before tn_b (local position 1).");
        Assert.True(bIndex < extraIndex, "the live-only column should come after every matched column.");
    }

    // ---- HasChanges ----

    [Fact]
    public async Task Preview_HasChanges_TrueOnlyForWritePlans()
    {
        var (service, _) = CreateService(Entity("""{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing", "AttributeType": "String" }"""));
        var local = Local(Attr("tn_existing", "String", schemaName: "tn_Existing")); // unchanged

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        Assert.False(preview.HasChanges);
    }

    [Fact]
    public async Task Preview_HasChanges_TrueForNewColumn()
    {
        var (service, _) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        Assert.True(preview.HasChanges);
    }

    // ---- ApplyAsync wiring ----

    [Fact]
    public async Task ApplyAsync_CreatePlan_CallsCreateAttribute()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        var call = Assert.Single(client.CreateAttributeCalls);
        Assert.Equal("tn_test", call.EntityLogicalName);
    }

    [Fact]
    public async Task ApplyAsync_LookupCreatePlan_CallsCreateOneToManyRelationship_NotCreateAttribute()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_lookup", "Lookup", schemaName: "tn_Lookup", configure: b => { b.RelationshipSchemaName = "tn_test_contact"; b.Targets = ["contact"]; }));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        Assert.Single(client.CreateOneToManyRelationshipCalls);
        Assert.Empty(client.CreateAttributeCalls);
    }

    [Fact]
    public async Task ApplyAsync_CustomerCreatePlan_CallsCreateCustomerRelationships()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_customer", "Customer", schemaName: "tn_Customer", configure: b => b.Targets = ["account", "contact"]));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        Assert.Single(client.CreateCustomerRelationshipsCalls);
    }

    [Fact]
    public async Task ApplyAsync_OptionRenamePlan_CallsUpdateOptionValue()
    {
        var existing = Entity("""
            { "LogicalName": "tn_choice", "SchemaName": "tn_Choice", "AttributeType": "Picklist" }
            """);
        var (service, client) = CreateService(existing);
        client.AttributeMetadataJson = (_, _) => """{ "LogicalName": "tn_choice", "SchemaName": "tn_Choice" }""";
        client.AttributeOptionSetJson = (_, _) => """
            { "OptionSet": { "IsGlobal": false, "Options": [ { "Value": 1, "Label": { "UserLocalizedLabel": { "Label": "Old" } } } ] } }
            """;

        var local = Local(Attr("tn_choice", "Picklist", schemaName: "tn_Choice", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "New" }]));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        Assert.Single(client.UpdateOptionValueCalls);
    }
}

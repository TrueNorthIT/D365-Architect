using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
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
        // Fixture carries "IsCustomAttribute": true, as the live API always
        // reports for a custom column. Without it tn_extra reads as a system
        // column, which the table import diff deliberately hides (see
        // "System columns in the diff" below).
        var existing = Entity("""
            { "LogicalName": "tn_b", "SchemaName": "tn_B", "AttributeType": "String" },
            { "LogicalName": "tn_extra", "SchemaName": "tn_Extra", "AttributeType": "String", "IsCustomAttribute": true },
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

    // ---- solutionUniqueName threading (the "new components silently don't
    // join the solution" gap found and fixed this session) ----

    [Fact]
    public async Task ApplyAsync_UseTransactionTrue_WithSolutionUniqueName_AttachesItToTheCreateWrite()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, useTransaction: true, solutionUniqueName: "JCTest", CancellationToken.None);

        var write = Assert.IsType<DataverseWrite.CreateAttribute>(Assert.Single(Assert.Single(client.ExecuteTransactionCalls)));
        Assert.Equal("JCTest", write.SolutionUniqueName);
    }

    [Fact]
    public async Task ApplyAsync_UseTransactionFalse_WithSolutionUniqueName_PassesItToCreateAttributeAsync()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, useTransaction: false, solutionUniqueName: "JCTest", CancellationToken.None);

        var call = Assert.Single(client.CreateAttributeCalls);
        Assert.Equal("JCTest", call.SolutionUniqueName);
    }

    [Fact]
    public async Task ApplyAsync_LookupCreatePlan_WithSolutionUniqueName_PassesItToCreateOneToManyRelationshipAsync()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_lookup", "Lookup", schemaName: "tn_Lookup", configure: b => { b.RelationshipSchemaName = "tn_test_contact"; b.Targets = ["contact"]; }));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, useTransaction: false, solutionUniqueName: "JCTest", CancellationToken.None);

        var call = Assert.Single(client.CreateOneToManyRelationshipCalls);
        Assert.Equal("JCTest", call.SolutionUniqueName);
    }

    [Fact]
    public async Task ApplyAsync_DefaultFourArgOverload_NeverAttachesASolutionUniqueName()
    {
        // The plain four-argument IImportService.ApplyAsync (what every
        // caller not scoping to a solution still uses) must keep the old
        // "wherever Dataverse's own default context puts it" behavior —
        // this is the regression guard for that default.
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        var write = Assert.IsType<DataverseWrite.CreateAttribute>(Assert.Single(Assert.Single(client.ExecuteTransactionCalls)));
        Assert.Null(write.SolutionUniqueName);
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

    // ---- useTransaction wiring ----

    [Fact]
    public async Task ApplyAsync_DefaultFourArgOverload_UsesTransaction_NotIndividualCalls()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        Assert.Single(client.ExecuteTransactionCalls);
        var writes = Assert.Single(client.ExecuteTransactionCalls);
        Assert.IsType<DataverseWrite.CreateAttribute>(Assert.Single(writes));
        // The fake dispatches ExecuteTransactionAsync writes into the same
        // lists individual calls use, so this also confirms nothing here
        // called CreateAttributeAsync directly.
        Assert.Single(client.CreateAttributeCalls);
    }

    [Fact]
    public async Task ApplyAsync_UseTransactionFalse_CallsIndividualMethods_NeverExecuteTransaction()
    {
        var (service, client) = CreateService(Entity(""));
        var local = Local(Attr("tn_new", "String", schemaName: "tn_New"));
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, useTransaction: false, solutionUniqueName: null, CancellationToken.None);


        Assert.Empty(client.ExecuteTransactionCalls);
        Assert.Single(client.CreateAttributeCalls);
    }

    [Fact]
    public async Task ApplyAsync_UseTransactionTrue_BatchesTableUpdateAndColumnPlansIntoOneCall_InOrder()
    {
        var existing = Entity("""{ "LogicalName": "tn_existing", "SchemaName": "tn_Existing", "AttributeType": "String" }""");
        var (service, client) = CreateService(existing);
        client.EntityMetadataJson = """{ "LogicalName": "tn_test", "SchemaName": "tn_Test" }""";
        var local = new EntityDefinition
        {
            LogicalName = "tn_test",
            DisplayName = "New Display Name",
            Attributes = [Attr("tn_existing", "String", schemaName: "tn_Existing"), Attr("tn_new", "String", schemaName: "tn_New")],
        };
        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", local, CancellationToken.None);

        await service.ApplyAsync(new Uri("https://test.crm.dynamics.com"), "token", preview, CancellationToken.None);

        var writes = Assert.Single(client.ExecuteTransactionCalls);
        Assert.Equal(2, writes.Count);
        Assert.IsType<DataverseWrite.UpdateEntity>(writes[0]);
        Assert.IsType<DataverseWrite.CreateAttribute>(writes[1]);
    }
}

/// <summary>
/// Issue #34 (sub-issue of #30, "Solution import fixes"): system columns
/// never listed as deletions in the table import diff, and table-level
/// properties that can't be applied are reported with a
/// reason. Kept apart from <see cref="TableImportServiceTests"/> only so
/// the issue's two changes read as one unit.
///
/// What "system column" means here: a live column that Dataverse reports as
/// not custom (<c>IsCustomAttribute</c> false, i.e.
/// <see cref="AttributeDefinition.IsCustomField"/> null once read) AND that
/// is absent from the local YAML. Those are the platform-owned columns every
/// table carries (createdby, modifiedby, ownerid, statecode, statuscode,
/// versionnumber, and their ...name/...yominame companions) - ones this
/// import can neither create nor delete (<see cref="AttributeImportAction.WouldRemove"/>
/// is never applied), so listing them as removals is pure noise. A
/// custom-flagged live-only column is NOT a system column and still shows.
/// </summary>
public sealed class TableImportServiceDiffNoiseAndUnappliedPropertyTests
{
    private static readonly Uri Environment = new("https://test.crm.dynamics.com");

    private static (TableImportService Service, FakeDataverseClient Client) CreateService(string existingEntityJson)
    {
        var client = new FakeDataverseClient
        {
            EntityDefinitionJson = existingEntityJson,
            // Option-bearing system columns (statecode/statuscode) make the
            // import fetch their option sets; an empty body means "no options".
            AttributeOptionSetJson = (_, _) => "{}",
            AttributeMetadataJson = (name, _) => $$"""{ "LogicalName": "{{name}}", "SchemaName": "{{name}}" }""",
        };
        var service = new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));
        return (service, client);
    }

    private static string Entity(string attributesJson, string extraEntityProperties = "") => $$"""
        {
          "LogicalName": "tn_test",
          "SchemaName": "tn_Test",
          "OwnershipType": "UserOwned",
          {{extraEntityProperties}}
          "Attributes": [ {{attributesJson}} ]
        }
        """;

    /// <summary>The platform-owned columns a real custom table always carries - the issue's own examples (createdby, modifiedby, ownerid, statecode) plus statuscode and a ...name companion.</summary>
    private const string SystemColumnsJson = """
        { "LogicalName": "createdby", "SchemaName": "CreatedBy", "AttributeType": "Lookup", "IsCustomAttribute": false, "Targets": ["systemuser"] },
        { "LogicalName": "createdbyname", "SchemaName": "CreatedByName", "AttributeType": "String", "IsCustomAttribute": false },
        { "LogicalName": "modifiedby", "SchemaName": "ModifiedBy", "AttributeType": "Lookup", "IsCustomAttribute": false, "Targets": ["systemuser"] },
        { "LogicalName": "ownerid", "SchemaName": "OwnerId", "AttributeType": "Owner", "IsCustomAttribute": false, "Targets": ["systemuser", "team"] },
        { "LogicalName": "statecode", "SchemaName": "StateCode", "AttributeType": "State", "IsCustomAttribute": false },
        { "LogicalName": "statuscode", "SchemaName": "StatusCode", "AttributeType": "Status", "IsCustomAttribute": false }
        """;

    private static readonly string[] SystemColumnNames = ["createdby", "createdbyname", "modifiedby", "ownerid", "statecode", "statuscode"];

    private static AttributeDefinition Attr(string name, string type, string schemaName, Action<AttributeDefinitionBuilder>? configure = null)
    {
        var builder = new AttributeDefinitionBuilder(type, schemaName) { Name = name };
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static List<string> RemovedLines(TableImportPreview preview) =>
        TextDiff.Compute(preview.ExistingYaml, preview.NewYaml)
            .Where(line => line.Kind == TextDiffLineKind.Removed)
            .Select(line => line.Text)
            .ToList();

    private static TablePropertyNotApplied SingleNotApplied(TableImportPreview preview)
    {
        Assert.True(preview.NotAppliedTableProperties.Count == 1,
            "expected exactly one table-level property reported as \"not applied: <reason>\" in TableImportPreview.NotAppliedTableProperties, but there were "
            + $"{preview.NotAppliedTableProperties.Count}: [{string.Join(", ", preview.NotAppliedTableProperties.Select(p => p.Property))}].");
        return preview.NotAppliedTableProperties[0];
    }

    // ---- System columns in the diff ----

    // Given a live table carrying the platform's own system columns that the
    // local YAML doesn't mention, when TableImportService.PreviewAsync builds
    // the diff (ExistingYaml vs NewYaml, what `table import` prints), then no
    // removed line names any of them.
    [Fact]
    public async Task PreviewAsync_LiveOnlySystemColumns_AreNeverShownAsRemovedInTheDiff()
    {
        var live = Entity("""{ "LogicalName": "tn_name", "SchemaName": "tn_Name", "AttributeType": "String", "IsCustomAttribute": true },""" + SystemColumnsJson);
        var (service, _) = CreateService(live);
        var local = new EntityDefinition { LogicalName = "tn_test", Attributes = [Attr("tn_name", "String", "tn_Name")] };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        var removed = RemovedLines(preview);
        var leaked = SystemColumnNames.Where(column => removed.Any(line => line.Contains(column, StringComparison.Ordinal))).ToList();
        Assert.True(leaked.Count == 0,
            $"expected no system columns in the diff as deletions, but these were listed as removed: {string.Join(", ", leaked)}.");
    }

    // Real changes still show: a genuine edit to a column both sides
    // have must still appear in the diff alongside the hidden system columns.
    // Given tn_name's live DisplayName is "Old Name" and the local YAML says
    // "New Name", then the diff removes the old text and adds the new.
    [Fact]
    public async Task PreviewAsync_RealColumnChange_StillShowsInTheDiffAlongsideHiddenSystemColumns()
    {
        var live = Entity("""{ "LogicalName": "tn_name", "SchemaName": "tn_Name", "AttributeType": "String", "IsCustomAttribute": true, "DisplayName": { "UserLocalizedLabel": { "Label": "Old Name" } } },""" + SystemColumnsJson);
        var (service, _) = CreateService(live);
        var local = new EntityDefinition { LogicalName = "tn_test", Attributes = [Attr("tn_name", "String", "tn_Name", b => b.DisplayName = "New Name")] };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        var diff = TextDiff.Compute(preview.ExistingYaml, preview.NewYaml);
        Assert.Contains(diff, line => line.Kind == TextDiffLineKind.Removed && line.Text.Contains("Old Name", StringComparison.Ordinal));
        Assert.Contains(diff, line => line.Kind == TextDiffLineKind.Added && line.Text.Contains("New Name", StringComparison.Ordinal));
    }

    // Only live-only columns are hidden: a system column the local
    // YAML DOES list is being compared, not deleted, so a real difference on
    // it must still show. Given live createdbyname's DisplayName is "Old"
    // and the local YAML sets "New", then the diff shows the change.
    [Fact]
    public async Task PreviewAsync_SystemColumnPresentInLocalYamlWithARealChange_StillShowsInTheDiff()
    {
        var live = Entity("""{ "LogicalName": "createdbyname", "SchemaName": "CreatedByName", "AttributeType": "String", "IsCustomAttribute": false, "DisplayName": { "UserLocalizedLabel": { "Label": "Old" } } }""");
        var (service, _) = CreateService(live);
        var local = new EntityDefinition { LogicalName = "tn_test", Attributes = [Attr("createdbyname", "String", "CreatedByName", b => b.DisplayName = "New")] };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        var diff = TextDiff.Compute(preview.ExistingYaml, preview.NewYaml);
        Assert.Contains(diff, line => line.Kind == TextDiffLineKind.Removed && line.Text.Contains("Old", StringComparison.Ordinal));
        Assert.Contains(diff, line => line.Kind == TextDiffLineKind.Added && line.Text.Contains("New", StringComparison.Ordinal));
    }

    // Definition boundary, a design decision this test records: a
    // live-only column flagged custom (IsCustomAttribute true) is not a
    // platform-owned system column, so it is still shown. Without this the
    // filter could be "hide every live-only column", which would also hide a
    // custom column someone dropped from the YAML by mistake.
    [Fact]
    public async Task PreviewAsync_LiveOnlyCustomColumn_IsStillShownAsRemovedInTheDiff()
    {
        var live = Entity("""{ "LogicalName": "tn_orphan", "SchemaName": "tn_Orphan", "AttributeType": "String", "IsCustomAttribute": true },""" + SystemColumnsJson);
        var (service, _) = CreateService(live);
        var local = new EntityDefinition { LogicalName = "tn_test", Attributes = [] };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        Assert.Contains(RemovedLines(preview), line => line.Contains("tn_orphan", StringComparison.Ordinal));
    }

    // ---- Table-level properties that can't be applied ----

    // Given the live table has HasNotes false and the local YAML asks for
    // hasNotes: true (this import only writes DisplayName/PluralDisplayName/
    // Description), when TableImportService.PreviewAsync runs, then
    // NotAppliedTableProperties lists "hasNotes" with a non-empty reason.
    [Fact]
    public async Task PreviewAsync_LocalHasNotesTrueButLiveFalse_ReportsHasNotesAsNotApplied()
    {
        var (service, _) = CreateService(Entity("", extraEntityProperties: "\"HasNotes\": false,"));
        var local = new EntityDefinition { LogicalName = "tn_test", HasNotes = true };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        var notApplied = SingleNotApplied(preview);
        Assert.Equal("hasNotes", notApplied.Property, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(notApplied.Reason),
            "a property that isn't applied must say why (\"not applied: <reason>\"), but the reason was empty.");
    }

    // The same applies to every other table-level property the model
    // documents as "applying this file back won't ...": HasActivities and
    // IsActivity (and, below, OwnershipType).
    [Theory]
    [InlineData("HasActivities")]
    [InlineData("IsActivity")]
    public async Task PreviewAsync_LocalBooleanTablePropertyTrueButLiveFalse_ReportsItAsNotApplied(string property)
    {
        var (service, _) = CreateService(Entity("", extraEntityProperties: $"\"{property}\": false,"));
        var local = new EntityDefinition
        {
            LogicalName = "tn_test",
            HasActivities = property == "HasActivities" ? true : null,
            IsActivity = property == "IsActivity" ? true : null,
        };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        var notApplied = SingleNotApplied(preview);
        Assert.Equal(property, notApplied.Property, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(notApplied.Reason), $"'{property}' must be reported with a reason.");
    }

    // OwnershipType can't be changed after creation (see
    // EntityDefinition.OwnershipType). Given live UserOwned and local
    // OrganizationOwned, then it is reported as not applied.
    [Fact]
    public async Task PreviewAsync_LocalOwnershipTypeDiffersFromLive_ReportsItAsNotApplied()
    {
        var (service, _) = CreateService(Entity(""));
        var local = new EntityDefinition { LogicalName = "tn_test", OwnershipType = "OrganizationOwned" };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        var notApplied = SingleNotApplied(preview);
        Assert.Equal("ownershipType", notApplied.Property, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(notApplied.Reason), "'ownershipType' must be reported with a reason.");
    }

    // No false alarms: when the local YAML already agrees with live,
    // nothing is "not applied". Given live HasNotes true and local
    // hasNotes: true, the list is empty.
    [Fact]
    public async Task PreviewAsync_LocalHasNotesMatchesLive_ReportsNothingAsNotApplied()
    {
        var (service, _) = CreateService(Entity("", extraEntityProperties: "\"HasNotes\": true,"));
        var local = new EntityDefinition { LogicalName = "tn_test", HasNotes = true };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        Assert.Empty(preview.NotAppliedTableProperties);
    }

    // Absent means "don't touch": a property the local YAML omits is
    // never reported (yaml-conventions Rule 1). Given a live table with
    // HasNotes true and a local YAML that doesn't mention it, the list is
    // empty.
    [Fact]
    public async Task PreviewAsync_LocalOmitsHasNotes_ReportsNothingAsNotApplied()
    {
        var (service, _) = CreateService(Entity("", extraEntityProperties: "\"HasNotes\": true,"));
        var local = new EntityDefinition { LogicalName = "tn_test" };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        Assert.Empty(preview.NotAppliedTableProperties);
    }

    // An un-appliable property is not a write. It must not make the
    // preview claim there is something to import (so the "Nothing to import"
    // summary - where the reason is shown - still fires), and no table
    // update body is built for it.
    [Fact]
    public async Task PreviewAsync_OnlyAnUnappliableTablePropertyDiffers_HasNoWritesToApply()
    {
        var (service, _) = CreateService(Entity("", extraEntityProperties: "\"HasNotes\": false,"));
        var local = new EntityDefinition { LogicalName = "tn_test", HasNotes = true };

        var preview = await service.PreviewAsync(Environment, "token", local, CancellationToken.None);

        Assert.False(preview.HasChanges, "An unappliable property must not count as a change to import.");
        Assert.Null(preview.TableUpdateBody);
    }
}

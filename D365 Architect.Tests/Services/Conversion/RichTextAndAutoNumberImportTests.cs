using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// <c>table import</c> planning and applying for rich text (REQ-13, 15, 16)
/// and auto-number columns (REQ-17, 19, 20, 21), through the real
/// <see cref="TableImportService"/> against a <see cref="FakeDataverseClient"/>.
///
/// Contract the developer must provide (frozen by these tests):
/// <list type="bullet">
/// <item><see cref="AttributeImportPlan.AutoNumberSeed"/> is set (to the
/// YAML's <c>autoNumberSeed</c>) only on a <see cref="AttributeImportAction.Create"/>
/// plan; null on every other plan.</item>
/// <item>Applying such a plan issues a <see cref="DataverseWrite.SetAutoNumberSeed"/>
/// (transaction path) / <see cref="IDataverseClient.SetAutoNumberSeedAsync"/>
/// (<c>--no-transaction</c>) after that column's create, with the column's
/// logical name. Whether it joins the same changeset is not pinned here;
/// see <see cref="FakeDataverseClient.ColumnWriteOrder"/>.</item>
/// <item>An <c>autoNumberSeed</c> on a column that already exists never
/// writes a seed. The plan, whatever its action, carries a warning
/// containing "seed ignored" (and "already exists").</item>
/// <item>A refused column plans as <see cref="AttributeImportAction.Invalid"/>
/// with the refusal in <see cref="AttributeImportPlan.Reason"/>, no
/// request body, and nothing written on apply.</item>
/// </list>
/// Tests marked "guard" already pass against the current code.
/// </summary>
public sealed class RichTextAndAutoNumberImportTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    private static (TableImportService Service, FakeDataverseClient Client) CreateService(string liveEntityJson, string? liveAttributeJson = null)
    {
        var client = new FakeDataverseClient { EntityDefinitionJson = liveEntityJson };
        if (liveAttributeJson is not null)
        {
            client.AttributeMetadataJson = (_, _) => liveAttributeJson;
        }

        return (new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client)), client);
    }

    private static AttributeDefinition Column(string type, string schemaName, Action<AttributeDefinitionBuilder>? configure = null)
    {
        var builder = new AttributeDefinitionBuilder(type, schemaName) { Name = schemaName.ToLowerInvariant() };
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static EntityDefinition Local(params AttributeDefinition[] attributes) => new() { LogicalName = "tn_test", Attributes = attributes };

    private static Task<TableImportPreview> PreviewAsync(TableImportService service, params AttributeDefinition[] attributes) =>
        service.PreviewAsync(EnvironmentUrl, "token", Local(attributes), CancellationToken.None);

    private static AttributeImportPlan PlanFor(TableImportPreview preview, string logicalName) =>
        preview.AttributePlans.Single(p => p.LogicalName == logicalName);

    private static readonly string NoLiveColumns = RichTextAndAutoNumberFixtures.Entity();

    // ---- REQ-13: rich text create ----

    // REQ-13 example: `type: Memo, format: RichText` -> the planned create body makes a rich text column.
    // Code path: TableImportService.BuildCreatePlanAsync -> AttributeMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    [Trait("Requirement", "REQ-13")]
    public async Task PreviewAsync_NewMemoWithRichTextFormat_PlansACreateOfARichTextColumn()
    {
        var (service, _) = CreateService(NoLiveColumns);

        var preview = await PreviewAsync(service, Column("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Format = "RichText"; b.MaxLength = 100000; }));

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.RichTextLogicalName);
        Expect.Equal(AttributeImportAction.Create, plan.Action, $"REQ-13: a new rich text Memo should plan as a create. Reason: <{plan.Reason}>.");
        Expect.Equal("RichText", (string?)plan.RequestBody?["FormatName"]?["Value"], "REQ-13: the create body should carry FormatName 'RichText'.");
    }

    // ---- REQ-15: a Memo format the tool can't create is refused, nothing written ----

    // REQ-15 example: `format: Json` -> "Memo format 'Json' isn't supported", nothing written.
    // Code path: TableImportService.BuildCreatePlanAsync -> AttributeChangeValidator.ValidateCreate.
    [Fact]
    [Trait("Requirement", "REQ-15")]
    public async Task PreviewAndApply_NewMemoWithUnsupportedFormat_IsInvalidAndWritesNothing()
    {
        var (service, client) = CreateService(NoLiveColumns);

        var preview = await PreviewAsync(service, Column("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => b.Format = "Json"));
        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.RichTextLogicalName);
        Expect.Equal(AttributeImportAction.Invalid, plan.Action, "REQ-15: a Memo with an unsupported format should plan as Invalid, not be created as a plain Memo.");
        Expect.Contains("Memo format 'Json' isn't supported", plan.Reason, "REQ-15: the plan should say why in the requirement's own wording.");
        Expect.True(plan.RequestBody is null, "REQ-15: a refused column should carry no request body.");
        Expect.True(!preview.HasChanges, "REQ-15: with only a refused column there is nothing to import.");
        Expect.Empty(client.CreateAttributeCalls, "REQ-15: nothing should be written for a refused column.");
        Expect.Empty(client.ExecuteTransactionCalls, "REQ-15: no transaction should be sent when there are no writes.");
    }

    // ---- REQ-16: switching plain <-> rich text is refused, with the reason in the plan ----

    // REQ-16 example: "Live plain column, YAML says RichText -> plan shows it skipped with the reason."
    // Pinned as AttributeImportAction.Invalid (shown as "! ... (not applied: reason)"); no update is attempted.
    // Code path: TableImportService.BuildUpdatePlanAsync -> AttributeChangeValidator.ValidateUpdate.
    [Fact]
    [Trait("Requirement", "REQ-16")]
    public async Task PreviewAndApply_LivePlainMemoWithYamlRichText_IsRefusedWithAReasonAndNotUpdated()
    {
        var (service, client) = CreateService(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.PlainMemo), RichTextAndAutoNumberFixtures.PlainMemo);

        var preview = await PreviewAsync(service, Column("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Format = "RichText"; b.MaxLength = 2000; }));
        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.RichTextLogicalName);
        Expect.Equal(AttributeImportAction.Invalid, plan.Action, "REQ-16: switching a live plain Memo to rich text should plan as Invalid (skipped), not Update.");
        Expect.Contains("rich text", plan.Reason?.ToLowerInvariant(), "REQ-16: the plan's reason should explain the plain/rich text switch isn't supported.");
        Expect.Empty(client.UpdateAttributeCalls, "REQ-16: no column update should be attempted.");
        Expect.Empty(client.ExecuteTransactionCalls, "REQ-16: no transaction should be sent when the only change is refused.");
    }

    // REQ-16, other direction: live rich text column (as read back from Dataverse), YAML says TextArea.
    // Code path: TableImportService.BuildUpdatePlanAsync -> AttributeChangeValidator.ValidateUpdate.
    [Fact]
    [Trait("Requirement", "REQ-16")]
    public async Task PreviewAsync_LiveRichTextMemoWithYamlTextArea_IsRefusedWithAReason()
    {
        var (service, _) = CreateService(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveRichTextMemo), RichTextAndAutoNumberFixtures.LiveRichTextMemo);

        var preview = await PreviewAsync(service, Column("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Format = "TextArea"; b.MaxLength = 100000; }));

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.RichTextLogicalName);
        Expect.Equal(AttributeImportAction.Invalid, plan.Action, "REQ-16: switching a live rich text Memo to plain should plan as Invalid (skipped), not Update.");
        Expect.Contains("rich text", plan.Reason?.ToLowerInvariant(), "REQ-16: the plan's reason should explain the plain/rich text switch isn't supported.");
    }

    // ---- REQ-17: auto-number create / validation ----

    // REQ-17 example: `autoNumberFormat: IVC{SEQNUM:9}` on a new String -> a create whose body carries the format.
    // Code path: TableImportService.BuildCreatePlanAsync -> AttributeMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    [Trait("Requirement", "REQ-17")]
    public async Task PreviewAsync_NewStringWithAutoNumberFormat_PlansACreateCarryingTheFormat()
    {
        var (service, _) = CreateService(NoLiveColumns);

        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "IVC{SEQNUM:9}"));

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal(AttributeImportAction.Create, plan.Action, $"REQ-17: a new auto-number String should plan as a create. Reason: <{plan.Reason}>.");
        Expect.Equal("IVC{SEQNUM:9}", (string?)plan.RequestBody?["AutoNumberFormat"], "REQ-17: the create body should carry the AutoNumberFormat.");
    }

    // REQ-17 scope: autoNumberFormat on a non-String column is refused in the plan, nothing written.
    // Code path: TableImportService.BuildCreatePlanAsync -> AttributeChangeValidator.ValidateCreate.
    [Fact]
    [Trait("Requirement", "REQ-17")]
    public async Task PreviewAsync_NewIntegerWithAutoNumberFormat_IsInvalid()
    {
        var (service, _) = CreateService(NoLiveColumns);

        var preview = await PreviewAsync(service, Column("Integer", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:6}"));

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal(AttributeImportAction.Invalid, plan.Action, "REQ-17: autoNumberFormat on an Integer should plan as Invalid.");
        Expect.Contains("autoNumberFormat", plan.Reason, "REQ-17: the reason should name autoNumberFormat.");
    }

    // ---- REQ-19: changing autoNumberFormat on an existing column ----

    // REQ-19 example: "`MSUM{SEQNUM:6}` -> `MSUM{SEQNUM:8}` shows as an update in the plan." Live read-back is the literal live JSON.
    // Code path: TableImportService.AttributesMatch (compare AutoNumberFormat) -> BuildUpdatePlanAsync -> ApplyUpdateFields.
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public async Task PreviewAsync_ChangedAutoNumberFormatOnALiveColumn_PlansAnUpdateWithTheNewFormat()
    {
        var (service, _) = CreateService(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveAutoNumberString), RichTextAndAutoNumberFixtures.LiveAutoNumberString);

        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:8}"));

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal(AttributeImportAction.Update, plan.Action, "REQ-19: MSUM{SEQNUM:6} -> MSUM{SEQNUM:8} should plan as an update.");
        Expect.Equal("MSUM{SEQNUM:8}", (string?)plan.RequestBody?["AutoNumberFormat"], "REQ-19: the update body should carry the new AutoNumberFormat.");
    }

    // REQ-19 (no spurious update): the same format as live is no change. Guard.
    // Code path: TableImportService.AttributesMatch.
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public async Task PreviewAsync_SameAutoNumberFormatAsLive_PlansNoChange()
    {
        var (service, _) = CreateService(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveAutoNumberString));

        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:6}"));

        Expect.Equal(AttributeImportAction.Unchanged, PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName).Action, "REQ-19: an unchanged autoNumberFormat should not plan an update.");
    }

    // ---- REQ-20: the seed is set once, only when the column is created in this run ----

    // REQ-20 / REQ-21 data: a new seeded column's Create plan carries the seed (what the printed plan line is built from).
    // Code path: TableImportService.BuildCreatePlanAsync -> AttributeImportPlan.AutoNumberSeed.
    [Fact]
    [Trait("Requirement", "REQ-21")]
    public async Task PreviewAsync_NewAutoNumberColumnWithSeed_PlanCarriesTheSeed()
    {
        var (service, _) = CreateService(NoLiveColumns);

        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.AutoNumberFormat = "MSUM{SEQNUM:6}"; b.AutoNumberSeed = 1; }));

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal(AttributeImportAction.Create, plan.Action, $"REQ-21: a new seeded auto-number column should plan as a create. Reason: <{plan.Reason}>.");
        Expect.Equal(1, plan.AutoNumberSeed, "REQ-21: the Create plan should carry the seed (1) so the plan can state it.");
    }

    // REQ-20 example: `autoNumberSeed: 1` on a new column -> after the create, SetAutoNumberSeed(tn_test, bogdan_ref, 1).
    // Transaction path (the default). Code path: TableImportService.BuildWrites -> DataverseWrite.SetAutoNumberSeed.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ApplyAsync_NewSeededColumn_SetsTheSeedAfterTheCreate_InTheTransactionPath()
    {
        var (service, client) = CreateService(NoLiveColumns);
        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.AutoNumberFormat = "MSUM{SEQNUM:6}"; b.AutoNumberSeed = 1; }));

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var seed = Assert.Single(client.SetAutoNumberSeedCalls);
        Expect.Equal(("tn_test", RichTextAndAutoNumberFixtures.AutoNumberLogicalName, 1), seed, "REQ-20: the seed should be set on the new column's table, logical name and value.");
        Expect.Sequence(["create:bogdan_Ref", "seed:bogdan_ref=1"], client.ColumnWriteOrder, "REQ-20: the seed must be written after the column it seeds has been created.");
    }

    // REQ-20, same behaviour for `table import --no-transaction`. Code path: TableImportService.ApplyAsync (useTransaction: false).
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ApplyAsync_NewSeededColumn_SetsTheSeedAfterTheCreate_InTheNoTransactionPath()
    {
        var (service, client) = CreateService(NoLiveColumns);
        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.AutoNumberFormat = "MSUM{SEQNUM:6}"; b.AutoNumberSeed = 1; }));

        await service.ApplyAsync(EnvironmentUrl, "token", preview, useTransaction: false, solutionUniqueName: null, CancellationToken.None);

        Expect.Sequence(["create:bogdan_Ref", "seed:bogdan_ref=1"], client.ColumnWriteOrder, "REQ-20: with --no-transaction the seed must still be written after its column's create.");
    }

    // REQ-20: with several seeded columns, each seed follows its own column's create (not just "somewhere later").
    // Code path: TableImportService.ApplyAsync / BuildWrites.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ApplyAsync_TwoNewSeededColumns_EachSeedFollowsItsOwnCreate()
    {
        var (service, client) = CreateService(NoLiveColumns);
        var preview = await PreviewAsync(service,
            Column("String", "bogdan_First", b => { b.AutoNumberFormat = "A{SEQNUM:4}"; b.AutoNumberSeed = 1; }),
            Column("String", "bogdan_Second", b => { b.AutoNumberFormat = "B{SEQNUM:4}"; b.AutoNumberSeed = 500; }));

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var order = client.ColumnWriteOrder;
        Expect.Equal(4, order.Count, $"REQ-20: two creates and two seeds expected. Actual: <{string.Join(", ", order)}>.");
        Expect.True(order.IndexOf("create:bogdan_First") < order.IndexOf("seed:bogdan_first=1"), $"REQ-20: bogdan_first's seed must follow its create. Order: <{string.Join(", ", order)}>.");
        Expect.True(order.IndexOf("create:bogdan_Second") < order.IndexOf("seed:bogdan_second=500"), $"REQ-20: bogdan_second's seed must follow its create. Order: <{string.Join(", ", order)}>.");
    }

    // REQ-20: the seed is optional - a new auto-number column without autoNumberSeed leaves the counter at Dataverse's own start. Guard.
    // Code path: TableImportService.ApplyAsync.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ApplyAsync_NewAutoNumberColumnWithoutSeed_SetsNoSeed()
    {
        var (service, client) = CreateService(NoLiveColumns);
        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:6}"));

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        Expect.Empty(client.SetAutoNumberSeedCalls, "REQ-20: no autoNumberSeed in the YAML means no SetAutoNumberSeed call.");
        Expect.Equal(1, client.CreateAttributeCalls.Count, "REQ-20: the column itself should still be created.");
    }

    // REQ-20: "re-importing the same file doesn't reset the counter." The column now exists live (the first run created it),
    // the YAML still carries autoNumberSeed: 1 -> plan is Unchanged, no seed is written, and the plan says the seed is ignored.
    // Code path: TableImportService.BuildAttributePlansAsync (existing column, seed present).
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ApplyAsync_SeedOnAnAlreadyExistingUnchangedColumn_WritesNoSeedAndSaysItIsIgnored()
    {
        var (service, client) = CreateService(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveAutoNumberString));
        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.AutoNumberFormat = "MSUM{SEQNUM:6}"; b.AutoNumberSeed = 1; }));

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal(AttributeImportAction.Unchanged, plan.Action, "REQ-20: a seed on an existing, otherwise unchanged column must not turn it into a change.");
        Expect.Empty(client.SetAutoNumberSeedCalls, "REQ-20: re-importing must never reset the counter on an existing column.");
        Assert.True(plan.Warnings is not null && plan.Warnings.Any(w => w.Contains("seed ignored", StringComparison.OrdinalIgnoreCase) && w.Contains("already exists", StringComparison.OrdinalIgnoreCase)),
            $"REQ-20: the plan should note 'seed ignored — column already exists'. Warnings: <{string.Join(" | ", plan.Warnings ?? [])}>.");
    }

    // REQ-20: same rule when the existing column IS being updated in this run (format changed): update applies, seed does not.
    // Code path: TableImportService.BuildUpdatePlanAsync + ApplyAsync.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task ApplyAsync_SeedOnAnExistingColumnBeingUpdated_UpdatesTheFormatButWritesNoSeed()
    {
        var (service, client) = CreateService(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveAutoNumberString), RichTextAndAutoNumberFixtures.LiveAutoNumberString);
        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.AutoNumberFormat = "MSUM{SEQNUM:8}"; b.AutoNumberSeed = 1; }));

        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal(AttributeImportAction.Update, plan.Action, "REQ-19/20: the changed format should still plan as an update.");
        Expect.True(plan.AutoNumberSeed is null, "REQ-20: an update plan must not carry a seed to write.");
        Expect.Equal(1, client.UpdateAttributeCalls.Count, "REQ-19: the format change should be applied.");
        Expect.Empty(client.SetAutoNumberSeedCalls, "REQ-20: an update must never reset the counter.");
        Assert.True(plan.Warnings is not null && plan.Warnings.Any(w => w.Contains("seed ignored", StringComparison.OrdinalIgnoreCase)),
            $"REQ-20: the update plan should note the seed is ignored. Warnings: <{string.Join(" | ", plan.Warnings ?? [])}>.");
    }

    // REQ-20: "`autoNumberSeed` without `autoNumberFormat` is refused" - planned as Invalid, nothing written.
    // Code path: TableImportService.BuildCreatePlanAsync -> AttributeChangeValidator.ValidateCreate.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public async Task PreviewAndApply_NewColumnWithSeedButNoFormat_IsInvalidAndWritesNothing()
    {
        var (service, client) = CreateService(NoLiveColumns);

        var preview = await PreviewAsync(service, Column("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberSeed = 1));
        await service.ApplyAsync(EnvironmentUrl, "token", preview, CancellationToken.None);

        var plan = PlanFor(preview, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal(AttributeImportAction.Invalid, plan.Action, "REQ-20: a seed with no autoNumberFormat should plan as Invalid.");
        Expect.Contains("autoNumberSeed", plan.Reason, "REQ-20: the reason should name autoNumberSeed.");
        Expect.Empty(client.CreateAttributeCalls, "REQ-20: nothing should be created for a refused column.");
        Expect.Empty(client.SetAutoNumberSeedCalls, "REQ-20: nothing should be seeded for a refused column.");
    }
}

using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Export side and round trip for rich text and auto-number
/// columns: <see cref="EntityJsonDefinitionReader"/> reading the live
/// shapes in <see cref="RichTextAndAutoNumberFixtures"/> (literal live
/// read-backs, 2026-10-26), <see cref="TableExportService"/> writing them,
/// and the YAML rebuilding into the create body / re-importing as a no-op.
///
/// Decision pinned here: a Memo's YAML <c>format</c> reads <c>RichText</c>
/// only when the live <c>FormatName.Value</c> says so (the legacy
/// <c>Format</c> reads <c>"Text"</c> for a rich column and is not trusted
/// for that); every other Memo keeps whatever the legacy <c>Format</c> says,
/// exactly as export writes it today (<c>TextArea</c> for a plain one).
/// A String's <c>autoNumberFormat</c> is read when non-empty; Dataverse
/// reports an ordinary column's as an empty string, which means "none" and
/// is left out of the YAML (default omission).
///
/// <c>autoNumberSeed</c> is never read or exported (it isn't column
/// metadata); that absence is not tested separately.
/// Tests marked "guard" already pass against the current code; they pin
/// output that must not change.
/// </summary>
public sealed class RichTextAndAutoNumberExportTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    private static AttributeDefinition Column(EntityDefinition entity, string logicalName) =>
        entity.Attributes.Single(a => a.Name == logicalName);

    private static async Task<string> ExportAsync(params string[] attributeJson)
    {
        var client = new FakeDataverseClient { EntityDefinitionJson = RichTextAndAutoNumberFixtures.Entity(attributeJson) };
        var service = new TableExportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));
        return await service.ExportTableAsync(EnvironmentUrl, "token", "tn_test", solutionUniqueName: null, CancellationToken.None);
    }

    // ---- reader ----

    // "Export shows a rich text column as `format: RichText`" - reader half.
    // Live: Format "Text" + FormatName.Value "RichText" must read as Format "RichText".
    // Code path: EntityJsonDefinitionReader.ParseAttribute (Format for Memo).
    [Fact]
    public void Read_RichTextMemo_ReadsFormatAsRichText()
    {
        var entity = new EntityJsonDefinitionReader().Read(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveRichTextMemo));

        var column = Column(entity, RichTextAndAutoNumberFixtures.RichTextLogicalName);
        Expect.Equal("Memo", column.Type, "the rich text column is still a Memo.");
        Expect.Equal("RichText", column.Format, "FormatName.Value 'RichText' should read as Format 'RichText'; the legacy Format 'Text' must not win.");
        Expect.Equal(100000, column.MaxLength, "MaxLength should still be read.");
    }

    // "a plain Memo exports as today" - the legacy Format is kept, with or without a FormatName. Guard.
    // Code path: EntityJsonDefinitionReader.ParseAttribute.
    [Theory]
    [InlineData(RichTextAndAutoNumberFixtures.PlainMemo)]
    [InlineData("""{ "LogicalName": "bogdan_notes", "SchemaName": "bogdan_Notes", "AttributeType": "Memo", "Format": "TextArea", "MaxLength": 2000 }""")]
    public void Read_PlainMemo_KeepsItsLegacyFormat(string attributeJson)
    {
        var entity = new EntityJsonDefinitionReader().Read(RichTextAndAutoNumberFixtures.Entity(attributeJson));

        Expect.Equal("TextArea", Column(entity, RichTextAndAutoNumberFixtures.RichTextLogicalName).Format, "a plain Memo should keep reading as Format 'TextArea', unchanged from today.");
    }

    // ---- reader ----

    // "Export writes `autoNumberFormat`" - reader half. Live: AutoNumberFormat "MSUM{SEQNUM:6}", Format/FormatName "Text".
    // Code path: EntityJsonDefinitionReader.ParseAttribute (AutoNumberFormat).
    [Fact]
    public void Read_AutoNumberString_ReadsTheAutoNumberFormat()
    {
        var entity = new EntityJsonDefinitionReader().Read(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveAutoNumberString));

        var column = Column(entity, RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal("MSUM{SEQNUM:6}", column.AutoNumberFormat, "the live AutoNumberFormat should be read into AttributeDefinition.AutoNumberFormat.");
        Expect.Equal("Text", column.Format, "an auto-number column's Format stays 'Text', as the export writes it today.");
    }

    // Default omission: an ordinary String reports AutoNumberFormat as "" - that is "none", not a value to export. Guard.
    // Code path: EntityJsonDefinitionReader.ParseAttribute.
    [Fact]
    public void Read_OrdinaryStringWithEmptyAutoNumberFormat_ReadsNoAutoNumberFormat()
    {
        var entity = new EntityJsonDefinitionReader().Read(RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.OrdinaryStringWithEmptyAutoNumberFormat));

        Assert.True(Column(entity, RichTextAndAutoNumberFixtures.AutoNumberLogicalName).AutoNumberFormat is null,
            "an empty live AutoNumberFormat should read as null so ordinary columns export without an autoNumberFormat key.");
    }

    // ---- the exported YAML ----

    // Example: "Exporting the rich column yields `format: RichText`."
    // Code path: TableExportService.ExportTableAsync -> EntityJsonDefinitionReader -> EntityYamlSerializer.
    [Fact]
    public async Task ExportTableAsync_RichTextColumn_WritesFormatRichText()
    {
        var yaml = await ExportAsync(RichTextAndAutoNumberFixtures.LiveRichTextMemo);

        Expect.Contains("format: RichText", yaml, "exporting the rich text column should write 'format: RichText'.");
    }

    // "a plain Memo exports as today": format: TextArea. Guard.
    // Code path: TableExportService.ExportTableAsync.
    [Fact]
    public async Task ExportTableAsync_PlainMemo_StillWritesFormatTextArea()
    {
        var yaml = await ExportAsync(RichTextAndAutoNumberFixtures.PlainMemo);

        Expect.Contains("format: TextArea", yaml, "a plain Memo should still export as 'format: TextArea'.");
    }

    // Example: "Exporting the column yields `autoNumberFormat: MSUM{SEQNUM:6}`."
    // The key is asserted by name and the value by reading the YAML back, so YAML quoting of the braces doesn't matter.
    // Code path: TableExportService.ExportTableAsync -> EntityYamlSerializer.
    [Fact]
    public async Task ExportTableAsync_AutoNumberColumn_WritesTheAutoNumberFormat()
    {
        var yaml = await ExportAsync(RichTextAndAutoNumberFixtures.LiveAutoNumberString);

        Expect.Contains("autoNumberFormat:", yaml, "exporting the auto-number column should write an 'autoNumberFormat' key.");
        var column = Column(EntityYamlDeserializer.FromYaml(yaml), RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        Expect.Equal("MSUM{SEQNUM:6}", column.AutoNumberFormat, "the exported autoNumberFormat should read back as 'MSUM{SEQNUM:6}'.");
    }

    // ---- Round trip: live JSON -> reader -> YAML -> create body ----

    // Rich text round trip: export the live rich text column, read the YAML, build the create body: FormatName RichText.
    // Code path: TableExportService -> EntityYamlDeserializer -> AttributeMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    public async Task ExportedYaml_RebuildsARichTextCreateBody()
    {
        var yaml = await ExportAsync(RichTextAndAutoNumberFixtures.LiveRichTextMemo);

        var column = Column(EntityYamlDeserializer.FromYaml(yaml), RichTextAndAutoNumberFixtures.RichTextLogicalName);
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(column);

        Expect.Equal("RichText", (string?)body["FormatName"]?["Value"], "export -> re-create should reproduce FormatName 'RichText'.");
        Expect.Equal(100000, (int?)body["MaxLength"], "export -> re-create should reproduce MaxLength 100000.");
    }

    // Auto-number round trip: export the live auto-number column, read the YAML, build the create body: AutoNumberFormat kept.
    // Code path: TableExportService -> EntityYamlDeserializer -> AttributeMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    public async Task ExportedYaml_RebuildsAnAutoNumberCreateBody()
    {
        var yaml = await ExportAsync(RichTextAndAutoNumberFixtures.LiveAutoNumberString);

        var column = Column(EntityYamlDeserializer.FromYaml(yaml), RichTextAndAutoNumberFixtures.AutoNumberLogicalName);
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(column);

        Expect.Equal("MSUM{SEQNUM:6}", (string?)body["AutoNumberFormat"], "export -> re-create should reproduce AutoNumberFormat 'MSUM{SEQNUM:6}'.");
        Expect.Equal("Text", (string?)body["FormatName"]?["Value"], "export -> re-create should reproduce FormatName 'Text'.");
    }

    // ---- Round trip: re-import against the same live JSON must be a no-op ----

    // Round trip (no spurious diff): export both live columns, then `table import` that YAML against the same live table.
    // Neither column may show as a change - in particular the legacy Format 'Text' on the rich column must not look different.
    // Guard (passes today because both sides read 'Text'; it must still pass once the reader says 'RichText').
    // Code path: TableExportService -> EntityYamlDeserializer -> TableImportService.PreviewAsync (AttributesMatch).
    [Fact]
    public async Task ExportedYaml_ReimportedAgainstTheSameLiveTable_PlansNoChangeForEitherColumn()
    {
        var liveJson = RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveRichTextMemo, RichTextAndAutoNumberFixtures.LiveAutoNumberString);
        var client = new FakeDataverseClient { EntityDefinitionJson = liveJson };
        var optionSetFetcher = new AttributeOptionSetFetcher(client);
        var exportedYaml = await new TableExportService(client, new EntityJsonDefinitionReader(), optionSetFetcher)
            .ExportTableAsync(EnvironmentUrl, "token", "tn_test", solutionUniqueName: null, CancellationToken.None);
        var local = EntityYamlDeserializer.FromYaml(exportedYaml);

        var preview = await new TableImportService(client, new EntityJsonDefinitionReader(), optionSetFetcher)
            .PreviewAsync(EnvironmentUrl, "token", local, CancellationToken.None);

        foreach (var logicalName in new[] { RichTextAndAutoNumberFixtures.RichTextLogicalName, RichTextAndAutoNumberFixtures.AutoNumberLogicalName })
        {
            var plan = preview.AttributePlans.Single(p => p.LogicalName == logicalName);
            Expect.Equal(AttributeImportAction.Unchanged, plan.Action, $"Round trip: re-importing the unmodified export of '{logicalName}' should plan no change.");
        }
    }

    // ---- YAML keys ----

    // The user-facing YAML keys are spelled `format`, `autoNumberFormat`, `autoNumberSeed`,
    // written unquoted as in the requirements' examples. Pins the spelling (guard: passes once the properties exist).
    // Code path: EntityYamlDeserializer.FromYaml -> AttributeDefinition.Format/AutoNumberFormat/AutoNumberSeed.
    [Fact]
    public void FromYaml_RichTextAndAutoNumberKeys_Populate()
    {
        const string yaml = """
            entity: tn_test
            attributes:
            - name: bogdan_notes
              schemaName: bogdan_Notes
              type: Memo
              format: RichText
              maxLength: 100000
            - name: bogdan_ref
              schemaName: bogdan_Ref
              type: String
              autoNumberFormat: IVC{SEQNUM:9}
              autoNumberSeed: 1
            """;

        var entity = EntityYamlDeserializer.FromYaml(yaml);

        Expect.Equal("RichText", Column(entity, "bogdan_notes").Format, "'format: RichText' should populate AttributeDefinition.Format.");
        Expect.Equal("IVC{SEQNUM:9}", Column(entity, "bogdan_ref").AutoNumberFormat, "'autoNumberFormat: IVC{SEQNUM:9}' should populate AttributeDefinition.AutoNumberFormat.");
        Expect.Equal(1, Column(entity, "bogdan_ref").AutoNumberSeed, "'autoNumberSeed: 1' should populate AttributeDefinition.AutoNumberSeed.");
    }
}

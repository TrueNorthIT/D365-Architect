using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Exports made before rich text support wrote a rich text column as
/// <c>format: Text</c> (its legacy <c>Format</c>; see
/// <see cref="RichTextAndAutoNumberFixtures.LiveRichTextMemo"/>). Re-importing
/// such a file against the live rich text column must be refused as a
/// plain/rich switch, with a reason that tells the user to re-export, never
/// planned as an update.
/// </summary>
public sealed class RichTextLegacyExportTests
{
    [Fact]
    public async Task PreviewAsync_PreRichTextExportAgainstLiveRichTextColumn_IsInvalidAndSaysToReExport()
    {
        var client = new FakeDataverseClient
        {
            EntityDefinitionJson = RichTextAndAutoNumberFixtures.Entity(RichTextAndAutoNumberFixtures.LiveRichTextMemo),
            AttributeMetadataJson = (_, _) => RichTextAndAutoNumberFixtures.LiveRichTextMemo,
        };
        var service = new TableImportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));
        var legacyColumn = AttributeDefinitionBuilder.Create("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b =>
        {
            b.Name = RichTextAndAutoNumberFixtures.RichTextLogicalName;
            b.Format = "Text";
            b.MaxLength = 100000;
        });

        var preview = await service.PreviewAsync(new Uri("https://test.crm.dynamics.com"), "token", new EntityDefinition { LogicalName = "tn_test", Attributes = [legacyColumn] }, CancellationToken.None);

        var plan = preview.AttributePlans.Single(p => p.LogicalName == RichTextAndAutoNumberFixtures.RichTextLogicalName);
        Expect.Equal(AttributeImportAction.Invalid, plan.Action, "A pre-rich-text export ('format: Text') of a live rich text column should be refused, not planned as an update.");
        Expect.Contains("re-export", plan.Reason, "The reason should tell the user to re-export the table.");
    }
}

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Live Web API shapes for the rich text and auto-number
/// tests. The first two are the literal read-backs confirmed
/// against a real environment on 2026-10-26 (create accepted, then read back):
/// <list type="bullet">
/// <item>A rich text column is a Memo whose legacy <c>Format</c> reads back
/// as <c>"Text"</c> - only <c>FormatName.Value</c> says <c>"RichText"</c>.</item>
/// <item>An auto-number column is a String whose <c>AutoNumberFormat</c> reads
/// back verbatim, with <c>Format</c>/<c>FormatName</c> both <c>"Text"</c>.</item>
/// </list>
/// The plain Memo is constructed (Microsoft Learn's documented Memo create
/// example uses <c>Format: "TextArea"</c>), not a live capture.
/// </summary>
public static class RichTextAndAutoNumberFixtures
{
    public const string RichTextSchemaName = "bogdan_Notes";
    public const string RichTextLogicalName = "bogdan_notes";
    public const string AutoNumberSchemaName = "bogdan_Ref";
    public const string AutoNumberLogicalName = "bogdan_ref";

    /// <summary>Live (2026-10-26): <c>Format: "Text"</c> does NOT reveal rich text; <c>FormatName</c> does.</summary>
    public const string LiveRichTextMemo = """
        {
          "LogicalName": "bogdan_notes",
          "SchemaName": "bogdan_Notes",
          "AttributeType": "Memo",
          "Format": "Text",
          "FormatName": { "Value": "RichText" },
          "MaxLength": 100000
        }
        """;

    /// <summary>Live (2026-10-26).</summary>
    public const string LiveAutoNumberString = """
        {
          "LogicalName": "bogdan_ref",
          "SchemaName": "bogdan_Ref",
          "AttributeType": "String",
          "Format": "Text",
          "FormatName": { "Value": "Text" },
          "MaxLength": 100,
          "AutoNumberFormat": "MSUM{SEQNUM:6}"
        }
        """;

    /// <summary>Constructed: an ordinary (plain text) Memo with the same logical name as <see cref="LiveRichTextMemo"/>.</summary>
    public const string PlainMemo = """
        {
          "LogicalName": "bogdan_notes",
          "SchemaName": "bogdan_Notes",
          "AttributeType": "Memo",
          "Format": "TextArea",
          "FormatName": { "Value": "TextArea" },
          "MaxLength": 2000
        }
        """;

    /// <summary>An ordinary String with a plain-text <c>Format</c>, as Dataverse reports a column that has no auto-number format ("" rather than absent).</summary>
    public const string OrdinaryStringWithEmptyAutoNumberFormat = """
        {
          "LogicalName": "bogdan_ref",
          "SchemaName": "bogdan_Ref",
          "AttributeType": "String",
          "Format": "Text",
          "FormatName": { "Value": "Text" },
          "MaxLength": 100,
          "AutoNumberFormat": ""
        }
        """;

    /// <summary>A live table <c>tn_test</c> carrying exactly the given attribute JSON objects.</summary>
    public static string Entity(params string[] attributeJson) => $$"""
        {
          "LogicalName": "tn_test",
          "SchemaName": "tn_Test",
          "OwnershipType": "UserOwned",
          "Attributes": [ {{string.Join(",", attributeJson)}} ]
        }
        """;
}

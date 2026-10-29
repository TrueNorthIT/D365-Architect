namespace D365Architect.Services.Dataverse;

/// <summary>
/// The Memo (multiple lines of text) formats this tool can create — what
/// <see cref="AttributeChangeValidator"/> accepts and
/// <see cref="AttributeMetadataJsonBuilder.BuildCreateBody"/> writes. Other
/// Memo formats exist in Dataverse (e.g. Email, Json) but no create shape for
/// them has been confirmed, so they're refused rather than quietly created as
/// a plain Memo.
///
/// <see cref="RichText"/> is carried by <c>FormatName</c>, not the legacy
/// <c>Format</c> property: confirmed live (2026-10-26) that a rich text
/// column reads back with <c>Format: "Text"</c> and only
/// <c>FormatName.Value</c> says <c>"RichText"</c>. Exports made before rich
/// text support therefore wrote such a column as <c>format: Text</c>.
/// </summary>
public static class MemoFormats
{
    /// <summary>The plain multi-line text column (Microsoft Learn's documented Memo create example).</summary>
    public const string TextArea = "TextArea";

    /// <summary>A rich text column (<c>FormatName: { Value: "RichText" }</c>).</summary>
    public const string RichText = "RichText";
}

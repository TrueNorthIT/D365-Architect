using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// The request bodies <see cref="AttributeMetadataJsonBuilder"/> builds for a
/// rich text Memo (REQ-13) and an auto-number String (REQ-17, REQ-19), and
/// that the seed (REQ-20) never leaks into column metadata.
///
/// Shapes are the live-confirmed ones (2026-10-26): a rich text column is
/// created with <c>FormatName: { Value: "RichText" }</c> and
/// <c>MaxLength: 100000</c>; an auto-number column with <c>AutoNumberFormat</c>
/// plus <c>FormatName: { Value: "Text" }</c> and <c>MaxLength: 100</c>
/// (Microsoft Learn, "Create autonumber columns":
/// https://learn.microsoft.com/power-apps/developer/data-platform/create-auto-number-attributes).
///
/// Tests marked "guard" already pass against the current code; they pin
/// behaviour the new work must not disturb.
/// </summary>
public sealed class RichTextAndAutoNumberBuilderTests
{
    private static AttributeDefinition Attr(string type, string schemaName, Action<AttributeDefinitionBuilder>? configure = null)
    {
        var builder = new AttributeDefinitionBuilder(type, schemaName) { Name = schemaName.ToLowerInvariant() };
        configure?.Invoke(builder);
        return builder.Build();
    }

    // ---- REQ-13: rich text create ----

    // REQ-13: "A Memo column with `format: RichText` is created as a rich text column."
    // Given a Memo with Format "RichText" and MaxLength 100000, BuildCreateBody sets FormatName.Value to "RichText".
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody, Memo case (today it hard-codes Format "TextArea" and ignores attribute.Format).
    [Fact]
    [Trait("Requirement", "REQ-13")]
    public void BuildCreateBody_MemoWithRichTextFormat_CreatesARichTextColumn()
    {
        var attribute = Attr("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Format = "RichText"; b.MaxLength = 100000; });

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        Expect.Equal("Microsoft.Dynamics.CRM.MemoAttributeMetadata", (string?)body["@odata.type"], "REQ-13: a rich text column is still a MemoAttributeMetadata.");
        Expect.Equal("RichText", (string?)body["FormatName"]?["Value"], "REQ-13: 'type: Memo, format: RichText' should send FormatName.Value 'RichText'.");
        Expect.Equal(100000, (int?)body["MaxLength"], "REQ-13: the requested MaxLength (100000, as confirmed live) should be sent unchanged.");
        Expect.True((string?)body["Format"] != "TextArea", "REQ-13: the body must not also say Format 'TextArea', which would contradict the rich text FormatName.");
    }

    // REQ-13 / REQ-15 (supported set): an explicit `format: TextArea` is the plain Memo the tool already creates. Guard.
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody, Memo case.
    [Fact]
    [Trait("Requirement", "REQ-15")]
    public void BuildCreateBody_MemoWithTextAreaFormat_StillCreatesAPlainMemo()
    {
        var attribute = Attr("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => b.Format = "TextArea");

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        Expect.Equal("TextArea", (string?)body["Format"], "REQ-15: 'format: TextArea' is a supported Memo format and should create the plain multi-line column as before.");
        Expect.True(body["FormatName"] is null || (string?)body["FormatName"]?["Value"] != "RichText", "REQ-15: a plain Memo must not be created as rich text.");
    }

    // ---- REQ-17: auto-number create ----

    // REQ-17: "A String column with `autoNumberFormat` is created as an auto-number column."
    // Example: `autoNumberFormat: IVC{SEQNUM:9}`. Live shape: AutoNumberFormat + FormatName Text + MaxLength 100.
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody, String case.
    [Fact]
    [Trait("Requirement", "REQ-17")]
    public void BuildCreateBody_StringWithAutoNumberFormat_SendsTheFormatOnAStringColumn()
    {
        var attribute = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "IVC{SEQNUM:9}");

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        Expect.Equal("Microsoft.Dynamics.CRM.StringAttributeMetadata", (string?)body["@odata.type"], "REQ-17: an auto-number column is a StringAttributeMetadata.");
        Expect.Equal("IVC{SEQNUM:9}", (string?)body["AutoNumberFormat"], "REQ-17: the create body should carry AutoNumberFormat exactly as given.");
        Expect.Equal("Text", (string?)body["FormatName"]?["Value"], "REQ-17: Dataverse requires an auto-number column's FormatName to be 'Text'.");
        Expect.Equal(100, (int?)body["MaxLength"], "REQ-17: the default MaxLength (100, as in the live-confirmed create) should be kept.");
    }

    // ---- REQ-19: auto-number update ----

    // REQ-19: "Changing `autoNumberFormat` on an existing column updates it."
    // Example: MSUM{SEQNUM:6} -> MSUM{SEQNUM:8}. The PUT body is the live column with the new format set.
    // Code path: AttributeMetadataJsonBuilder.ApplyUpdateFields, String case.
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public void ApplyUpdateFields_StringWithChangedAutoNumberFormat_SetsTheNewFormatOnTheBody()
    {
        var existing = JsonNode.Parse(RichTextAndAutoNumberFixtures.LiveAutoNumberString)!.AsObject();
        var local = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:8}");

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, local);

        Expect.Equal("MSUM{SEQNUM:8}", (string?)existing["AutoNumberFormat"], "REQ-19: the update body should carry the new AutoNumberFormat.");
        Expect.Equal("Text", (string?)existing["FormatName"]?["Value"], "REQ-19: the live FormatName 'Text' should be kept on the update body.");
    }

    // REQ-19 (adding a format): Microsoft Learn - "You can modify an existing format text column to be an autonumber format."
    // Code path: AttributeMetadataJsonBuilder.ApplyUpdateFields, String case.
    [Fact]
    [Trait("Requirement", "REQ-19")]
    public void ApplyUpdateFields_OrdinaryStringGivenAnAutoNumberFormat_SetsItOnTheBody()
    {
        var existing = JsonNode.Parse(RichTextAndAutoNumberFixtures.OrdinaryStringWithEmptyAutoNumberFormat)!.AsObject();
        var local = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:6}");

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, local);

        Expect.Equal("MSUM{SEQNUM:6}", (string?)existing["AutoNumberFormat"], "REQ-19: giving an existing ordinary String an autoNumberFormat should put it on the update body.");
    }

    // ---- REQ-13 / REQ-16: an update must not downgrade rich text ----

    // Guard for REQ-16: updating another field of a live rich text column (here MaxLength) PUTs the whole cloned
    // column back, so its FormatName 'RichText' must survive; losing it would silently turn rich text into plain.
    // Code path: AttributeMetadataJsonBuilder.ApplyUpdateFields, Memo case.
    [Fact]
    [Trait("Requirement", "REQ-16")]
    public void ApplyUpdateFields_RichTextMemoWithNewMaxLength_KeepsItRichText()
    {
        var existing = JsonNode.Parse(RichTextAndAutoNumberFixtures.LiveRichTextMemo)!.AsObject();
        var local = Attr("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Format = "RichText"; b.MaxLength = 50000; });

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, local);

        Expect.Equal(50000, (int?)existing["MaxLength"], "REQ-16: the new MaxLength should be applied.");
        Expect.Equal("RichText", (string?)existing["FormatName"]?["Value"], "REQ-16: updating a rich text column must keep its FormatName 'RichText'.");
    }

    // ---- REQ-20: the seed is not column metadata ----

    // REQ-20: "An optional `autoNumberSeed` sets the starting number" via SetAutoNumberSeed, never via the column body
    // (the column POST has no such property). A seeded column's create body is therefore identical to an unseeded one. Guard.
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    [Trait("Requirement", "REQ-20")]
    public void BuildCreateBody_AutoNumberColumnWithSeed_IsIdenticalToTheSameColumnWithoutOne()
    {
        var withSeed = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.AutoNumberFormat = "MSUM{SEQNUM:6}"; b.AutoNumberSeed = 1; });
        var withoutSeed = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:6}");

        var seeded = AttributeMetadataJsonBuilder.BuildCreateBody(withSeed).ToJsonString();
        var unseeded = AttributeMetadataJsonBuilder.BuildCreateBody(withoutSeed).ToJsonString();

        Expect.Equal(unseeded, seeded, "REQ-20: the seed is set by a separate SetAutoNumberSeed call, so the column's own create body must not change because a seed was given.");
    }
}

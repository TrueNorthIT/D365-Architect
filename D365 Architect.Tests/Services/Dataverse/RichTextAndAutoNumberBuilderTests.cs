using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// The request bodies <see cref="AttributeMetadataJsonBuilder"/> builds for a
/// rich text Memo and an auto-number String, and
/// that the seed never leaks into column metadata.
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

    // ---- rich text create ----

    // "A Memo column with `format: RichText` is created as a rich text column."
    // Given a Memo with Format "RichText" and MaxLength 100000, BuildCreateBody sets FormatName.Value to "RichText".
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody, Memo case (today it hard-codes Format "TextArea" and ignores attribute.Format).
    [Fact]
    public void BuildCreateBody_MemoWithRichTextFormat_CreatesARichTextColumn()
    {
        var attribute = Attr("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Format = "RichText"; b.MaxLength = 100000; });

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        Expect.Equal("Microsoft.Dynamics.CRM.MemoAttributeMetadata", (string?)body["@odata.type"], "a rich text column is still a MemoAttributeMetadata.");
        Expect.Equal("RichText", (string?)body["FormatName"]?["Value"], "'type: Memo, format: RichText' should send FormatName.Value 'RichText'.");
        Expect.Equal(100000, (int?)body["MaxLength"], "the requested MaxLength (100000, as confirmed live) should be sent unchanged.");
        Expect.True((string?)body["Format"] != "TextArea", "the body must not also say Format 'TextArea', which would contradict the rich text FormatName.");
    }

    // Supported set: an explicit `format: TextArea` is the plain Memo the tool already creates. Guard.
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody, Memo case.
    [Fact]
    public void BuildCreateBody_MemoWithTextAreaFormat_StillCreatesAPlainMemo()
    {
        var attribute = Attr("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => b.Format = "TextArea");

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        Expect.Equal("TextArea", (string?)body["Format"], "'format: TextArea' is a supported Memo format and should create the plain multi-line column as before.");
        Expect.True(body["FormatName"] is null || (string?)body["FormatName"]?["Value"] != "RichText", "a plain Memo must not be created as rich text.");
    }

    // ---- auto-number create ----

    // "A String column with `autoNumberFormat` is created as an auto-number column."
    // Example: `autoNumberFormat: IVC{SEQNUM:9}`. Live shape: AutoNumberFormat + FormatName Text + MaxLength 100.
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody, String case.
    [Fact]
    public void BuildCreateBody_StringWithAutoNumberFormat_SendsTheFormatOnAStringColumn()
    {
        var attribute = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "IVC{SEQNUM:9}");

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        Expect.Equal("Microsoft.Dynamics.CRM.StringAttributeMetadata", (string?)body["@odata.type"], "an auto-number column is a StringAttributeMetadata.");
        Expect.Equal("IVC{SEQNUM:9}", (string?)body["AutoNumberFormat"], "the create body should carry AutoNumberFormat exactly as given.");
        Expect.Equal("Text", (string?)body["FormatName"]?["Value"], "Dataverse requires an auto-number column's FormatName to be 'Text'.");
        Expect.Equal(100, (int?)body["MaxLength"], "the default MaxLength (100, as in the live-confirmed create) should be kept.");
    }

    // ---- auto-number update ----

    // "Changing `autoNumberFormat` on an existing column updates it."
    // Example: MSUM{SEQNUM:6} -> MSUM{SEQNUM:8}. The PUT body is the live column with the new format set.
    // Code path: AttributeMetadataJsonBuilder.ApplyUpdateFields, String case.
    [Fact]
    public void ApplyUpdateFields_StringWithChangedAutoNumberFormat_SetsTheNewFormatOnTheBody()
    {
        var existing = JsonNode.Parse(RichTextAndAutoNumberFixtures.LiveAutoNumberString)!.AsObject();
        var local = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:8}");

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, local);

        Expect.Equal("MSUM{SEQNUM:8}", (string?)existing["AutoNumberFormat"], "the update body should carry the new AutoNumberFormat.");
        Expect.Equal("Text", (string?)existing["FormatName"]?["Value"], "the live FormatName 'Text' should be kept on the update body.");
    }

    // Adding a format: Microsoft Learn - "You can modify an existing format text column to be an autonumber format."
    // Code path: AttributeMetadataJsonBuilder.ApplyUpdateFields, String case.
    [Fact]
    public void ApplyUpdateFields_OrdinaryStringGivenAnAutoNumberFormat_SetsItOnTheBody()
    {
        var existing = JsonNode.Parse(RichTextAndAutoNumberFixtures.OrdinaryStringWithEmptyAutoNumberFormat)!.AsObject();
        var local = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:6}");

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, local);

        Expect.Equal("MSUM{SEQNUM:6}", (string?)existing["AutoNumberFormat"], "giving an existing ordinary String an autoNumberFormat should put it on the update body.");
    }

    // ---- an update must not downgrade rich text ----

    // Guard against a rich text downgrade: updating another field of a live rich text column (here MaxLength) PUTs the whole cloned
    // column back, so its FormatName 'RichText' must survive; losing it would silently turn rich text into plain.
    // Code path: AttributeMetadataJsonBuilder.ApplyUpdateFields, Memo case.
    [Fact]
    public void ApplyUpdateFields_RichTextMemoWithNewMaxLength_KeepsItRichText()
    {
        var existing = JsonNode.Parse(RichTextAndAutoNumberFixtures.LiveRichTextMemo)!.AsObject();
        var local = Attr("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Format = "RichText"; b.MaxLength = 50000; });

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, local);

        Expect.Equal(50000, (int?)existing["MaxLength"], "the new MaxLength should be applied.");
        Expect.Equal("RichText", (string?)existing["FormatName"]?["Value"], "updating a rich text column must keep its FormatName 'RichText'.");
    }

    // ---- the seed is not column metadata ----

    // "An optional `autoNumberSeed` sets the starting number" via SetAutoNumberSeed, never via the column body
    // (the column POST has no such property). A seeded column's create body is therefore identical to an unseeded one. Guard.
    // Code path: AttributeMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    public void BuildCreateBody_AutoNumberColumnWithSeed_IsIdenticalToTheSameColumnWithoutOne()
    {
        var withSeed = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.AutoNumberFormat = "MSUM{SEQNUM:6}"; b.AutoNumberSeed = 1; });
        var withoutSeed = Attr("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => b.AutoNumberFormat = "MSUM{SEQNUM:6}");

        var seeded = AttributeMetadataJsonBuilder.BuildCreateBody(withSeed).ToJsonString();
        var unseeded = AttributeMetadataJsonBuilder.BuildCreateBody(withoutSeed).ToJsonString();

        Expect.Equal(unseeded, seeded, "the seed is set by a separate SetAutoNumberSeed call, so the column's own create body must not change because a seed was given.");
    }
}

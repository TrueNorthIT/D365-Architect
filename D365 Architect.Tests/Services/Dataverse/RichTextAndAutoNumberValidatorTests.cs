using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// What <see cref="AttributeChangeValidator"/> must refuse (rather than
/// silently ignore) for rich text / Memo formats and for
/// <c>autoNumberFormat</c> / <c>autoNumberSeed</c>. A
/// refusal is a non-null message from <c>ValidateCreate</c>/<c>ValidateUpdate</c>;
/// <c>table import</c> turns it into an <c>Invalid</c> plan and writes nothing.
///
/// Message wording the developer must match is quoted from the requirements
/// ("Memo format 'Json' isn't supported"); for the rest the tests look for the
/// words a maker needs to act on the message.
/// Tests marked "guard" already pass against the current code; they pin
/// input that must stay accepted.
/// </summary>
public sealed class RichTextAndAutoNumberValidatorTests
{
    private static AttributeDefinition Memo(string? format, int? maxLength = null) =>
        AttributeDefinitionBuilder.Create("Memo", RichTextAndAutoNumberFixtures.RichTextSchemaName, b => { b.Name = RichTextAndAutoNumberFixtures.RichTextLogicalName; b.Format = format; b.MaxLength = maxLength; });

    private static AttributeDefinition Str(string? autoNumberFormat, int? autoNumberSeed = null) =>
        AttributeDefinitionBuilder.Create("String", RichTextAndAutoNumberFixtures.AutoNumberSchemaName, b => { b.Name = RichTextAndAutoNumberFixtures.AutoNumberLogicalName; b.AutoNumberFormat = autoNumberFormat; b.AutoNumberSeed = autoNumberSeed; });

    // ---- a Memo format the tool can't create is refused on create ----

    // "A Memo format the tool can't create is refused, not silently ignored."
    // Example: `format: Json` -> "Memo format 'Json' isn't supported", nothing written.
    // Code path: AttributeChangeValidator.ValidateCreate (Memo format check).
    [Theory]
    [InlineData("Json")]
    [InlineData("Email")]
    public void ValidateCreate_MemoWithAnUncreatableFormat_IsRefusedNamingTheFormat(string format)
    {
        var error = AttributeChangeValidator.ValidateCreate(Memo(format));

        Assert.True(error is not null, $"a Memo with 'format: {format}' can't be created by this tool and should be refused, not created as a plain Memo.");
        Expect.Contains($"Memo format '{format}' isn't supported", error, "the refusal should name the unsupported format in the requirement's own wording.");
    }

    // The formats that ARE supported: plain TextArea, RichText, and no format at all (plain, as today). Guard for the first and third.
    // Code path: AttributeChangeValidator.ValidateCreate.
    [Theory]
    [InlineData(null)]
    [InlineData("TextArea")]
    [InlineData("RichText")]
    public void ValidateCreate_MemoWithASupportedFormat_IsAccepted(string? format)
    {
        var error = AttributeChangeValidator.ValidateCreate(Memo(format));

        Expect.True(error is null, $"a Memo with format '{format ?? "(none)"}' is creatable and should not be refused. Actual refusal: <{error}>.");
    }

    // ---- updating an existing Memo's format ----

    // "Switching an existing Memo between plain and rich text is refused with a clear message rather than attempted."
    // Example: live plain column, YAML says RichText. The message must say it is a plain <-> rich switch.
    // Code path: AttributeChangeValidator.ValidateUpdate (Memo format check).
    [Fact]
    public void ValidateUpdate_PlainMemoToRichText_IsRefusedAsASwitch()
    {
        var error = AttributeChangeValidator.ValidateUpdate(Memo("RichText"), Memo("TextArea"));

        Assert.True(error is not null, "switching a plain Memo to rich text isn't supported by Dataverse's column update and should be refused, not attempted.");
        Expect.Contains("rich text", error!.ToLowerInvariant(), "the refusal should say this is a rich text switch.");
        Expect.Contains("plain", error.ToLowerInvariant(), "the refusal should say it is a switch between plain and rich text.");
    }

    // Rich text switch, other direction: live rich text column, YAML says TextArea.
    // Code path: AttributeChangeValidator.ValidateUpdate (Memo format check).
    [Fact]
    public void ValidateUpdate_RichTextMemoToPlain_IsRefusedAsASwitch()
    {
        var error = AttributeChangeValidator.ValidateUpdate(Memo("TextArea"), Memo("RichText"));

        Assert.True(error is not null, "switching a rich text Memo to plain would lose formatting and should be refused, not attempted.");
        Expect.Contains("rich text", error!.ToLowerInvariant(), "the refusal should say this is a rich text switch.");
        Expect.Contains("plain", error.ToLowerInvariant(), "the refusal should say it is a switch between plain and rich text.");
    }

    // Unsupported format on update: today ApplyUpdateFields ignores a Memo's Format entirely, so `format: Json` on a live plain
    // column would be reported as an update and silently do nothing. It must be refused instead.
    // Code path: AttributeChangeValidator.ValidateUpdate (Memo format check).
    [Fact]
    public void ValidateUpdate_MemoToAnUnsupportedFormat_IsRefusedNamingTheFormat()
    {
        var error = AttributeChangeValidator.ValidateUpdate(Memo("Json"), Memo("TextArea"));

        Assert.True(error is not null, "changing a Memo to 'format: Json' can't be applied and should be refused, not silently ignored.");
        Expect.Contains("Memo format 'Json' isn't supported", error, "the refusal should name the unsupported format in the requirement's own wording.");
    }

    // Guard: a Memo whose YAML format already equals the live one is not a change and must stay accepted,
    // otherwise every export -> re-import of an existing rich text (or plain) column would be refused.
    // Code path: AttributeChangeValidator.ValidateUpdate.
    [Theory]
    [InlineData("RichText", "RichText")]
    [InlineData("TextArea", "TextArea")]
    [InlineData(null, "RichText")] // format left out of the YAML = don't touch
    public void ValidateUpdate_MemoFormatSameAsLiveOrUnspecified_IsAccepted(string? localFormat, string liveFormat)
    {
        var error = AttributeChangeValidator.ValidateUpdate(Memo(localFormat), Memo(liveFormat));

        Expect.True(error is null, $"local format '{localFormat ?? "(none)"}' against live '{liveFormat}' is not a switch and should not be refused. Actual refusal: <{error}>.");
    }

    // ---- autoNumberFormat is String-only ----

    // Scope (implied by "A String column with `autoNumberFormat`"): autoNumberFormat on any other type is refused
    // rather than dropped, so the maker isn't left thinking a Memo/Integer/DateTime is numbering itself.
    // Code path: AttributeChangeValidator.ValidateCreate (shared checks).
    [Theory]
    [InlineData("Integer")]
    [InlineData("Memo")]
    [InlineData("DateTime")]
    public void ValidateCreate_AutoNumberFormatOnANonStringColumn_IsRefused(string type)
    {
        var attribute = AttributeDefinitionBuilder.Create(type, "bogdan_Ref", b => { b.Name = "bogdan_ref"; b.AutoNumberFormat = "MSUM{SEQNUM:6}"; });

        var error = AttributeChangeValidator.ValidateCreate(attribute);

        Assert.True(error is not null, $"autoNumberFormat on a {type} column should be refused; only String columns can auto-number.");
        Expect.Contains("autoNumberFormat", error, "the refusal should name the autoNumberFormat key.");
        Expect.Contains("String", error, "the refusal should say only String columns support it.");
    }

    // Update side: same refusal for an existing non-String column.
    // Code path: AttributeChangeValidator.ValidateUpdate (shared checks).
    [Fact]
    public void ValidateUpdate_AutoNumberFormatOnAnExistingIntegerColumn_IsRefused()
    {
        var local = AttributeDefinitionBuilder.Create("Integer", "bogdan_Ref", b => { b.Name = "bogdan_ref"; b.AutoNumberFormat = "MSUM{SEQNUM:6}"; });
        var existing = AttributeDefinitionBuilder.Create("Integer", "bogdan_Ref", b => b.Name = "bogdan_ref");

        var error = AttributeChangeValidator.ValidateUpdate(local, existing);

        Assert.True(error is not null, "autoNumberFormat on an existing Integer column should be refused.");
        Expect.Contains("autoNumberFormat", error, "the refusal should name the autoNumberFormat key.");
    }

    // Happy path: a String with a format (and optionally a seed) is accepted for create. Guard.
    // Code path: AttributeChangeValidator.ValidateCreate.
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void ValidateCreate_StringWithAutoNumberFormat_IsAccepted(int? seed)
    {
        var error = AttributeChangeValidator.ValidateCreate(Str("MSUM{SEQNUM:6}", seed));

        Expect.True(error is null, $"a String with an autoNumberFormat (seed: {seed?.ToString() ?? "none"}) should be accepted. Actual refusal: <{error}>.");
    }

    // ---- a seed needs a format ----

    // "`autoNumberSeed` without `autoNumberFormat` is refused" - there is no sequence to seed.
    // Code path: AttributeChangeValidator.ValidateCreate (shared checks).
    [Fact]
    public void ValidateCreate_AutoNumberSeedWithoutAutoNumberFormat_IsRefused()
    {
        var error = AttributeChangeValidator.ValidateCreate(Str(autoNumberFormat: null, autoNumberSeed: 1));

        Assert.True(error is not null, "autoNumberSeed on a column with no autoNumberFormat should be refused.");
        Expect.Contains("autoNumberSeed", error, "the refusal should name autoNumberSeed.");
        Expect.Contains("autoNumberFormat", error, "the refusal should say autoNumberFormat is required.");
    }

    // Seed, update side: the same refusal applies to a column that already exists.
    // Code path: AttributeChangeValidator.ValidateUpdate (shared checks).
    [Fact]
    public void ValidateUpdate_AutoNumberSeedWithoutAutoNumberFormat_IsRefused()
    {
        var error = AttributeChangeValidator.ValidateUpdate(Str(autoNumberFormat: null, autoNumberSeed: 1), Str("MSUM{SEQNUM:6}"));

        Assert.True(error is not null, "autoNumberSeed in the YAML without an autoNumberFormat should be refused even when the live column has a format.");
        Expect.Contains("autoNumberSeed", error, "the refusal should name autoNumberSeed.");
    }
}

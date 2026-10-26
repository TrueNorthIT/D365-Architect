using System.Xml.Schema;
using D365Architect.Services.Conversion;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="FormXmlValidationMessage.IsKnownHarmless"/> - the narrow
/// allowlist of schema violations confirmed safe against real Dataverse
/// output, which gates whether `form import` blocks by default (see
/// <see cref="FormXmlValidationMessage"/>'s own doc comment for why this is
/// deliberately narrow rather than a general severity signal).
/// </summary>
public sealed class FormXmlValidationMessageTests
{
    private static FormXmlValidationMessage Message(string message) =>
        new(XmlSeverityType.Error, 1, 1, message, "", 0);

    [Theory]
    [InlineData("The 'headerdensity' attribute is not declared.")]
    [InlineData("The 'showinformselector' attribute is not declared.")]
    [InlineData("The element 'parameters' has invalid child element 'UClientActivitiesConfigurationJSON'.")]
    [InlineData("The element 'parameters' has invalid child element 'UClientNotesConfigurationJSON'.")]
    public void IsKnownHarmless_ConfirmedSafePatterns_ReturnsTrue(string message)
    {
        Assert.True(Message(message).IsKnownHarmless);
    }

    [Theory]
    [InlineData("The element 'parameters' has invalid child element 'TypeName'.")]
    [InlineData("The class id cannot be null for control element...")]
    [InlineData("The 'someotherattribute' attribute is not declared.")]
    public void IsKnownHarmless_EveryOtherViolation_ReturnsFalse(string message)
    {
        // Confirmed live: both of these exact shapes were once waved
        // through on the same "schema vs. real Dataverse disagree
        // sometimes" reasoning and were rejected by Dataverse with a raw
        // 400 - a violation must never be assumed safe by extension just
        // because it looks structurally similar to an allowlisted one.
        Assert.False(Message(message).IsKnownHarmless);
    }

    [Fact]
    public void IsKnownHarmless_MatchIsCaseSensitiveOnTheQuotedName()
    {
        // The allowlist matches the validator's own quoted attribute/element
        // name literally - a message that merely mentions the word (wrong
        // case, or not quoted the same way) must not be waved through.
        Assert.False(Message("The 'HeaderDensity' attribute is not declared.").IsKnownHarmless);
    }

    // ---- Further already-live patterns the validator must accept (issue #33) ----

    // Given the .NET schema validator's own message for text directly inside
    // a control's <parameters> (copied verbatim from real validator output),
    // FormXmlValidationMessage.IsKnownHarmless returns true.
    [Fact]
    public void IsKnownHarmless_TextDirectlyInsideControlParameters_ReturnsTrue()
    {
        var message = Message("The element 'parameters' cannot contain text. List of possible elements expected: 'Url, PassParameters, Security, Scrolling, Border'.");

        Assert.True(message.IsKnownHarmless,
            "expected the mixed-text <parameters> shape (PhoneNumberControl/ValidatedFieldControl/BusinessCardReader) to be known-harmless, but IsKnownHarmless was false.");
    }

    // A HideQuickFormBorder child of <parameters> (Quick View control) is
    // accepted as harmless. Message copied verbatim from real validator output.
    [Fact]
    public void IsKnownHarmless_HideQuickFormBorderChildOfParameters_ReturnsTrue()
    {
        var message = Message("The element 'parameters' has invalid child element 'HideQuickFormBorder'. List of possible elements expected: 'Url, PassParameters, Security, Scrolling, Border'.");

        Assert.True(message.IsKnownHarmless,
            "expected an invalid 'HideQuickFormBorder' child of <parameters> (Quick View control) to be known-harmless, but IsKnownHarmless was false.");
    }

    // Text inside an
    // element other than <parameters> is not part of any allowlisted shape.
    [Theory]
    [InlineData("The element 'cell' cannot contain text. List of possible elements expected: 'labels, control'.")]
    [InlineData("The element 'tab' cannot contain text. List of possible elements expected: 'labels, columns'.")]
    public void IsKnownHarmless_TextInsideAnyOtherElement_ReturnsFalse(string message)
    {
        Assert.False(Message(message).IsKnownHarmless,
            "only text inside a control's <parameters> is allowlisted; text elsewhere is still a genuine violation.");
    }

    // ---- End to end: real FormXmlValidator output for each shape in issue #33 ----
    //
    // The tests above pin IsKnownHarmless against message text; these pin it
    // against what FormXmlValidator.Validate actually emits for each
    // real-world shape, so a change to the validator's wording or to how
    // violations are split can't silently re-block an unchanged form.
    // The control classid used is representative only - the schema
    // validator doesn't look at it.

    private const string FormTemplate =
        "<form>" +
        "<tabs><tab id=\"{F72AA69D-9D27-9724-7862-9D1B2AB60DC8}\" name=\"tab_1\">" +
        "<labels><label description=\"General\" languagecode=\"1033\" /></labels>" +
        "<columns><column width=\"100%\"><sections><section id=\"{8DF70B50-80BE-47E9-AF08-5934CAEFC072}\" name=\"section_1\">" +
        "<rows><row><cell id=\"{6B3A8804-648A-82A0-38EE-E63836DB6018}\">" +
        "<labels><label description=\"Field\" languagecode=\"1033\" /></labels>" +
        "CONTROL" +
        "</cell></row></rows></section></sections></column></columns></tab></tabs></form>";

    private static string FormWithControl(string controlXml) => FormTemplate.Replace("CONTROL", controlXml, StringComparison.Ordinal);

    public static TheoryData<string, string> HarmlessRealWorldShapes => new()
    {
        // headerdensity="HighWithControls" on <form>.
        { "headerdensity=\"HighWithControls\" on <form>",
            FormWithControl("<control id=\"name\" classid=\"{4273EDBD-AC1D-40D3-9FB2-095C621B552D}\" datafieldname=\"name\" />")
                .Replace("<form>", "<form headerdensity=\"HighWithControls\">", StringComparison.Ordinal) },

        // mixed-text <parameters> (PhoneNumberControl shape).
        { "<parameters>telephone1</parameters>",
            FormWithControl("<control id=\"telephone1\" classid=\"{4273EDBD-AC1D-40D3-9FB2-095C621B552D}\" datafieldname=\"telephone1\"><parameters>telephone1</parameters></control>") },

        // mixed-text <parameters> (BusinessCardReader shape).
        { "<parameters>businesscard...</parameters>",
            FormWithControl("<control id=\"businesscard\" classid=\"{4273EDBD-AC1D-40D3-9FB2-095C621B552D}\"><parameters>businesscard</parameters></control>") },

        // UClientNotesConfigurationJSON first.
        { "<parameters> with UClientNotesConfigurationJSON and UClientActivitiesConfigurationJSON",
            FormWithControl("<control id=\"notescontrol\" classid=\"{06375649-C143-495E-A496-C962E5B4488E}\"><parameters><UClientNotesConfigurationJSON>{}</UClientNotesConfigurationJSON><UClientActivitiesConfigurationJSON>{}</UClientActivitiesConfigurationJSON></parameters></control>") },

        // UClientActivitiesConfigurationJSON first (order must not matter).
        { "<parameters> with UClientActivitiesConfigurationJSON first",
            FormWithControl("<control id=\"notescontrol\" classid=\"{06375649-C143-495E-A496-C962E5B4488E}\"><parameters><UClientActivitiesConfigurationJSON>{}</UClientActivitiesConfigurationJSON><UClientNotesConfigurationJSON>{}</UClientNotesConfigurationJSON></parameters></control>") },

        // HideQuickFormBorder on a Quick View control.
        { "<parameters> with HideQuickFormBorder (Quick View control)",
            FormWithControl("<control id=\"qv\" classid=\"{5C5600E0-1D6E-4205-A272-BE80DA87FD42}\"><parameters><HideQuickFormBorder>true</HideQuickFormBorder></parameters></control>") },
    };

    [Theory]
    [MemberData(nameof(HarmlessRealWorldShapes))]
    public void Validate_RealWorldShapeFromIssue33_EveryViolationIsKnownHarmless(string shape, string formXml)
    {
        var violations = FormXmlValidator.Validate(formXml);

        // Guard against the test passing vacuously: each shape is a genuine
        // schema violation, so the validator must report something.
        Assert.True(violations.Count > 0,
            $"Precondition: expected FormXmlValidator to report a violation for '{shape}', but it reported none - this test would pass vacuously.");

        var blocking = violations.Where(v => !v.IsKnownHarmless).Select(v => v.Message).ToList();
        Assert.True(blocking.Count == 0,
            $"'{shape}' should be accepted as harmless, but these violations would still block the import: {string.Join(" | ", blocking)}");
    }

    // A stray TypeName
    // inside <parameters> is the confirmed live 400 case; it must remain
    // blocking even when it sits beside allowlisted content. The invalid
    // child comes first: the .NET validator stops after the first invalid
    // sibling, so a TypeName placed after HideQuickFormBorder would be masked
    // (an inherent limitation, noted beside the allowlist).
    [Fact]
    public void Validate_InvalidChildOfParametersBesideAllowlistedContent_StillHasABlockingViolation()
    {
        var formXml = FormWithControl("<control id=\"qv\" classid=\"{5C5600E0-1D6E-4205-A272-BE80DA87FD42}\"><parameters><TypeName>x</TypeName><HideQuickFormBorder>true</HideQuickFormBorder></parameters></control>");

        var violations = FormXmlValidator.Validate(formXml);

        Assert.Contains(violations, v => !v.IsKnownHarmless && v.Message.Contains("'TypeName'", StringComparison.Ordinal));
    }

    // The HideQuickFormBorder allowance is limited to <parameters>.
    [Fact]
    public void IsKnownHarmless_HideQuickFormBorderUnderAnotherParent_ReturnsFalse()
    {
        Assert.False(Message("The element 'cell' has invalid child element 'HideQuickFormBorder'. List of possible elements expected: 'labels, control'.").IsKnownHarmless);
    }

    // An undeclared attribute that is not on the allowlist must keep
    // blocking, even on the same <form> element as headerdensity.
    [Fact]
    public void Validate_FormWithHeaderDensityAndAnUnknownAttribute_StillHasABlockingViolation()
    {
        var formXml = FormWithControl("<control id=\"name\" classid=\"{4273EDBD-AC1D-40D3-9FB2-095C621B552D}\" datafieldname=\"name\" />")
            .Replace("<form>", "<form headerdensity=\"HighWithControls\" notarealattribute=\"x\">", StringComparison.Ordinal);

        var violations = FormXmlValidator.Validate(formXml);

        Assert.Contains(violations, v => !v.IsKnownHarmless && v.Message.Contains("'notarealattribute'", StringComparison.Ordinal));
    }
}

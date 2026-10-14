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
}

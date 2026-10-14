using D365Architect.Services.Conversion;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

public sealed class DefaultValueConventionsTests
{
    [Theory]
    [InlineData("None", null)]
    [InlineData("none", null)]
    [InlineData("NONE", null)]
    [InlineData("ApplicationRequired", "ApplicationRequired")]
    [InlineData(null, null)]
    public void RequiredLevelOrNull_TreatsNoneCaseInsensitivelyAsDefault(string? input, string? expected) =>
        Assert.Equal(expected, DefaultValueConventions.RequiredLevelOrNull(input));

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, null)]
    [InlineData(null, null)]
    public void TrueOrNull_OnlyKeepsTrue(bool? input, bool? expected) =>
        Assert.Equal(expected, DefaultValueConventions.TrueOrNull(input));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, null)]
    [InlineData(null, null)]
    public void FalseOrNull_OnlyKeepsFalse(bool? input, bool? expected) =>
        Assert.Equal(expected, DefaultValueConventions.FalseOrNull(input));

    [Fact]
    public void BooleanOptionLabelOrNull_MatchingDefault_ReturnsNull()
    {
        Assert.Null(DefaultValueConventions.BooleanOptionLabelOrNull("True", "True"));
    }

    [Fact]
    public void BooleanOptionLabelOrNull_DifferentFromDefault_ReturnsValue()
    {
        Assert.Equal("Yes", DefaultValueConventions.BooleanOptionLabelOrNull("Yes", "True"));
    }

    [Fact]
    public void BooleanOptionLabelOrNull_IsCaseSensitive()
    {
        // Ordinal comparison, not OrdinalIgnoreCase - "true" != "True".
        Assert.Equal("true", DefaultValueConventions.BooleanOptionLabelOrNull("true", "True"));
    }
}

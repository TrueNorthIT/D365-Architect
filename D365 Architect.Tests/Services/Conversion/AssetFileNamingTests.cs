using D365Architect.Services.Conversion;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

public sealed class AssetFileNamingTests
{
    [Theory]
    [InlineData("Active Accounts", "active-accounts")]
    [InlineData("My View", "my-view")]
    [InlineData("  Trim Me  ", "trim-me")]
    [InlineData("My, View!", "my-view")]
    [InlineData("Already-Hyphenated", "already-hyphenated")]
    [InlineData("Multiple   Spaces", "multiple-spaces")]
    public void Slugify_ProducesExpectedSlug(string input, string expected) =>
        Assert.Equal(expected, AssetFileNaming.Slugify(input));

    [Fact]
    public void Slugify_AllPunctuation_FallsBackToUnnamed()
    {
        Assert.Equal("unnamed", AssetFileNaming.Slugify("!!!"));
    }

    [Fact]
    public void Slugify_EmptyString_FallsBackToUnnamed()
    {
        Assert.Equal("unnamed", AssetFileNaming.Slugify(""));
    }

    [Fact]
    public void MakeUnique_FirstUse_ReturnsStemUnchanged()
    {
        var used = new HashSet<string>();
        Assert.Equal("my-view", AssetFileNaming.MakeUnique("my-view", used));
    }

    [Fact]
    public void MakeUnique_CollidingStem_AppendsIncrementingSuffix()
    {
        var used = new HashSet<string>();
        AssetFileNaming.MakeUnique("my-view", used);

        Assert.Equal("my-view-2", AssetFileNaming.MakeUnique("my-view", used));
        Assert.Equal("my-view-3", AssetFileNaming.MakeUnique("my-view", used));
    }

    [Fact]
    public void MakeUnique_RecordsEveryReturnedCandidateInUsedStems()
    {
        var used = new HashSet<string>();
        var first = AssetFileNaming.MakeUnique("stem", used);
        Assert.Contains(first, used);
    }
}

using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

public sealed class GlobalChoiceChangeValidatorTests
{
    private static GlobalChoiceDefinition Choice(string name, IReadOnlyList<AttributeOptionDefinition>? options = null) => new() { Name = name, Options = options };

    [Fact]
    public void ValidateCreate_ValidNameAndOptions_Succeeds()
    {
        var choice = Choice("tn_colors", [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);
        Assert.Null(GlobalChoiceChangeValidator.ValidateCreate(choice));
    }

    [Theory]
    [InlineData("nocustomizationprefix")]
    [InlineData("tn-colors")]
    [InlineData("tn colors")]
    public void ValidateCreate_InvalidNamePattern_Fails(string name)
    {
        var choice = Choice(name, [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);
        Assert.NotNull(GlobalChoiceChangeValidator.ValidateCreate(choice));
    }

    [Fact]
    public void ValidateCreate_NoOptions_Fails()
    {
        var choice = Choice("tn_empty", options: null);
        var error = GlobalChoiceChangeValidator.ValidateCreate(choice);
        Assert.NotNull(error);
        Assert.Contains("Options", error);
    }

    [Fact]
    public void ValidateCreate_EmptyOptionsList_Fails()
    {
        var choice = Choice("tn_empty", options: []);
        Assert.NotNull(GlobalChoiceChangeValidator.ValidateCreate(choice));
    }

    [Fact]
    public void ValidateCreate_DuplicateOptionValues_Fails()
    {
        var choice = Choice("tn_dup", [
            new AttributeOptionDefinition { Value = 1, Label = "A" },
            new AttributeOptionDefinition { Value = 1, Label = "B" },
        ]);
        var error = GlobalChoiceChangeValidator.ValidateCreate(choice);
        Assert.NotNull(error);
        Assert.Contains("duplicate", error, StringComparison.OrdinalIgnoreCase);
    }
}

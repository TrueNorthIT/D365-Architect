using D365Architect.Services.Conversion;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>Covers <see cref="GlobalChoiceJsonReader"/>, including the same CRLF-normalization fix <see cref="EntityJsonDefinitionReaderTests"/> covers for tables.</summary>
public sealed class GlobalChoiceJsonReaderTests
{
    [Fact]
    public void Read_ParsesNameDisplayNameDescriptionAndOptions()
    {
        var json = """
            {
              "Name": "tn_colors",
              "DisplayName": { "UserLocalizedLabel": { "Label": "Colors" } },
              "Description": { "UserLocalizedLabel": { "Label": "A choice of colors" } },
              "Options": [
                { "Value": 1, "Label": { "UserLocalizedLabel": { "Label": "Red" } } },
                { "Value": 2, "Label": { "UserLocalizedLabel": { "Label": "Blue" } } }
              ]
            }
            """;

        var choice = GlobalChoiceJsonReader.Read(json);

        Assert.Equal("tn_colors", choice.Name);
        Assert.Equal("Colors", choice.DisplayName);
        Assert.Equal("A choice of colors", choice.Description);
        Assert.Equal(2, choice.Options!.Count);
        Assert.Equal(1, choice.Options![0].Value);
        Assert.Equal("Red", choice.Options![0].Label);
    }

    [Fact]
    public void Read_MissingName_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GlobalChoiceJsonReader.Read("""{ "DisplayName": null }"""));
    }

    [Fact]
    public void Read_MalformedJson_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => GlobalChoiceJsonReader.Read("{ not json"));
    }

    [Fact]
    public void Read_NoOptions_LeavesOptionsNull()
    {
        var choice = GlobalChoiceJsonReader.Read("""{ "Name": "tn_empty" }""");
        Assert.Null(choice.Options);
    }

    [Fact]
    public void Read_DescriptionWithCrlf_NormalizesToLf()
    {
        var json = """{ "Name": "tn_x", "Description": { "UserLocalizedLabel": { "Label": "Para one.\r\n\r\nPara two." } } }""";
        var choice = GlobalChoiceJsonReader.Read(json);
        Assert.Equal("Para one.\n\nPara two.", choice.Description);
    }

    [Fact]
    public void ReadMany_ParsesEveryChoiceInTheValueArray()
    {
        var json = """
            {
              "value": [
                { "Name": "tn_a" },
                { "Name": "tn_b" }
              ]
            }
            """;

        var choices = GlobalChoiceJsonReader.ReadMany(json, allowedMetadataIds: null);

        Assert.Equal(["tn_a", "tn_b"], choices.Select(c => c.Name));
    }

    [Fact]
    public void ReadMany_MissingValueArray_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GlobalChoiceJsonReader.ReadMany("""{ "notvalue": [] }""", null));
    }

    [Fact]
    public void ReadMany_AllowedMetadataIds_FiltersToOnlyThoseIds()
    {
        var keptId = Guid.NewGuid();
        var droppedId = Guid.NewGuid();
        var json = $$"""
            {
              "value": [
                { "Name": "kept", "MetadataId": "{{keptId}}" },
                { "Name": "dropped", "MetadataId": "{{droppedId}}" }
              ]
            }
            """;

        var choices = GlobalChoiceJsonReader.ReadMany(json, new HashSet<Guid> { keptId });

        Assert.Equal("kept", Assert.Single(choices).Name);
    }
}

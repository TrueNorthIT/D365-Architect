using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Schema;
using Xunit;

namespace D365Architect.Tests.Services.Schema;

/// <summary>
/// Covers <see cref="YamlSchemaGenerator"/> - in particular the dictionary
/// regression found and fixed alongside fe72321's <c>Translations</c>
/// properties: a dictionary-typed property (e.g.
/// <c>IReadOnlyDictionary&lt;int, string&gt;</c>) matched the generic
/// <c>IEnumerable</c> case, which assumes exactly one generic argument, and
/// silently produced an empty-object array schema instead of a proper map.
/// </summary>
public sealed class YamlSchemaGeneratorTests
{
    [Fact]
    public void Generate_DictionaryProperty_ProducesObjectSchemaWithStringValuedAdditionalProperties()
    {
        var schema = YamlSchemaGenerator.Generate(typeof(FormTab), "Tab", "A form tab");

        var translations = schema["properties"]!["translations"]!;

        Assert.Equal("object", (string)translations["type"]!);
        Assert.Equal("string", (string)translations["additionalProperties"]!["type"]!);
    }

    [Fact]
    public void Generate_ListProperty_StillProducesAnArraySchema()
    {
        // Guards the fallback IEnumerable path the dictionary check above
        // was deliberately inserted ahead of - a plain list must still come
        // out as an array, not get caught by the new dictionary branch.
        var schema = YamlSchemaGenerator.Generate(typeof(FormTab), "Tab", "A form tab");

        var columns = schema["properties"]!["columns"]!;

        Assert.Equal("array", (string)columns["type"]!);
    }

    [Fact]
    public void Generate_RootObjectType_SetsSchemaMetadata()
    {
        var schema = YamlSchemaGenerator.Generate(typeof(FormTab), "Tab", "A form tab");

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string)schema["$schema"]!);
        Assert.Equal("Tab", (string)schema["title"]!);
        Assert.Equal("A form tab", (string)schema["description"]!);
        Assert.Equal("object", (string)schema["type"]!);
    }
}

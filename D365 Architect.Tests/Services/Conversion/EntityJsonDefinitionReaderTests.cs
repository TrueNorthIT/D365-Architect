using D365Architect.Services.Conversion;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="EntityJsonDefinitionReader"/> — in particular the three
/// live-confirmed bugs found and fixed against a real Dataverse tenant this
/// tool never wants to regress on:
/// <list type="bullet">
/// <item>A MultiSelectPicklist column's live <c>AttributeType</c> reports
/// itself as <c>"Virtual"</c>, not <c>"MultiSelectPicklist"</c> — the reader
/// has to undo that before anything else keys off <c>Type</c>.</item>
/// <item>A genuinely local (non-shared) Picklist/MultiSelectPicklist option
/// set can populate <em>both</em> <c>OptionSet</c> and <c>GlobalOptionSet</c>
/// in the response — only <c>GlobalOptionSet</c>'s own <c>IsGlobal</c> flag
/// (not just its presence) says whether it's actually global-choice-bound.</item>
/// <item>A multi-line label containing <c>\r\n</c> must be normalized to
/// <c>\n</c> on read, or it never round-trips through YAML byte-identical.</item>
/// </list>
/// </summary>
public sealed class EntityJsonDefinitionReaderTests
{
    private readonly EntityJsonDefinitionReader _reader = new();

    private static string Entity(string attributesJson) => $$"""
        {
          "LogicalName": "tn_test",
          "SchemaName": "tn_Test",
          "DisplayName": { "UserLocalizedLabel": { "Label": "Test", "LanguageCode": 1033 } },
          "DisplayCollectionName": { "UserLocalizedLabel": { "Label": "Tests", "LanguageCode": 1033 } },
          "Description": { "UserLocalizedLabel": { "Label": "A test table", "LanguageCode": 1033 } },
          "OwnershipType": "UserOwned",
          "IsActivity": false,
          "HasActivities": false,
          "HasNotes": false,
          "Attributes": [ {{attributesJson}} ]
        }
        """;

    [Fact]
    public void Read_ParsesBasicEntityFields()
    {
        var result = _reader.Read(Entity(""));

        Assert.Equal("tn_test", result.LogicalName);
        Assert.Equal("tn_Test", result.SchemaName);
        Assert.Equal("Test", result.DisplayName);
        Assert.Equal("Tests", result.PluralDisplayName);
        Assert.Equal("A test table", result.Description);
        Assert.Equal("UserOwned", result.OwnershipType);
        // false is Dataverse's own default for all three - never stated.
        Assert.Null(result.IsActivity);
        Assert.Null(result.HasActivities);
        Assert.Null(result.HasNotes);
        Assert.Empty(result.Attributes);
    }

    [Fact]
    public void Read_MissingLogicalName_Throws()
    {
        var json = """{ "SchemaName": "tn_Test" }""";
        Assert.Throws<InvalidDataException>(() => _reader.Read(json));
    }

    [Fact]
    public void Read_MalformedJson_ThrowsInvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => _reader.Read("{ not json"));
    }

    [Fact]
    public void Read_AttributeMissingLogicalName_Throws()
    {
        var json = Entity("""{ "SchemaName": "tn_Foo", "AttributeType": "String" }""");
        Assert.Throws<InvalidDataException>(() => _reader.Read(json));
    }

    [Fact]
    public void Read_PlainStringAttribute_ParsesCoreFields()
    {
        var json = Entity("""
            {
              "LogicalName": "tn_name",
              "SchemaName": "tn_Name",
              "AttributeType": "String",
              "MaxLength": 100,
              "Format": "Text",
              "IsCustomAttribute": true,
              "RequiredLevel": { "Value": "ApplicationRequired" },
              "IsValidForAdvancedFind": { "Value": true }
            }
            """);

        var attribute = Assert.Single(_reader.Read(json).Attributes);
        Assert.Equal("tn_name", attribute.Name);
        Assert.Equal("tn_Name", attribute.SchemaName);
        Assert.Equal("String", attribute.Type);
        Assert.Equal(100, attribute.MaxLength);
        Assert.Equal("Text", attribute.Format);
        Assert.True(attribute.IsCustomField);
        Assert.Equal("ApplicationRequired", attribute.RequiredLevel);
        Assert.True(attribute.ValidForAdvancedFind);
    }

    [Fact]
    public void Read_UnknownAttributeType_DefaultsToUnknown()
    {
        var json = Entity("""{ "LogicalName": "tn_foo", "SchemaName": "tn_Foo" }""");
        var attribute = Assert.Single(_reader.Read(json).Attributes);
        Assert.Equal("Unknown", attribute.Type);
    }

    [Fact]
    public void Read_AllowedAttributeMetadataIds_FiltersToOnlyThoseIds()
    {
        var keptId = Guid.NewGuid();
        var droppedId = Guid.NewGuid();
        var json = $$"""
            {
              "LogicalName": "tn_test",
              "SchemaName": "tn_Test",
              "OwnershipType": "UserOwned",
              "Attributes": [
                { "LogicalName": "kept", "SchemaName": "Kept", "AttributeType": "String", "MetadataId": "{{keptId}}" },
                { "LogicalName": "dropped", "SchemaName": "Dropped", "AttributeType": "String", "MetadataId": "{{droppedId}}" }
              ]
            }
            """;

        var result = _reader.Read(json, new HashSet<Guid> { keptId });

        var attribute = Assert.Single(result.Attributes);
        Assert.Equal("kept", attribute.Name);
    }

    // ---- MultiSelectPicklist "Virtual" normalization (round 1 / round 5 regression) ----

    [Fact]
    public void Read_MultiSelectPicklist_NormalizesVirtualAttributeTypeBack()
    {
        // Confirmed live: a MultiSelectPicklist column's own AttributeType
        // reports as "Virtual", with AttributeTypeName.Value carrying the
        // real answer instead.
        var json = Entity("""
            {
              "LogicalName": "tn_multi",
              "SchemaName": "tn_Multi",
              "AttributeType": "Virtual",
              "AttributeTypeName": { "Value": "MultiSelectPicklistType" }
            }
            """);

        var attribute = Assert.Single(_reader.Read(json).Attributes);
        Assert.Equal("MultiSelectPicklist", attribute.Type);
    }

    [Fact]
    public void Read_GenuinelyVirtualAttribute_IsNotMisreadAsMultiSelectPicklist()
    {
        // A real Virtual attribute (AttributeTypeName not "MultiSelectPicklistType")
        // must stay "Virtual" - the normalization is specific to that one
        // confirmed AttributeTypeName value, not "AttributeType == Virtual" alone.
        var json = Entity("""
            {
              "LogicalName": "tn_calc",
              "SchemaName": "tn_Calc",
              "AttributeType": "Virtual",
              "AttributeTypeName": { "Value": "SomeOtherVirtualType" }
            }
            """);

        var attribute = Assert.Single(_reader.Read(json).Attributes);
        Assert.Equal("Virtual", attribute.Type);
    }

    [Fact]
    public void ListOptionBearingAttributes_RecognizesNormalizedMultiSelectPicklist()
    {
        var json = Entity("""
            {
              "LogicalName": "tn_multi",
              "SchemaName": "tn_Multi",
              "AttributeType": "Virtual",
              "AttributeTypeName": { "Value": "MultiSelectPicklistType" }
            }
            """);

        var found = EntityJsonDefinitionReader.ListOptionBearingAttributes(json);

        var entry = Assert.Single(found);
        Assert.Equal("tn_multi", entry.LogicalName);
        Assert.Equal("MultiSelectPicklist", entry.Type);
    }

    [Fact]
    public void ListOptionBearingAttributes_IgnoresPlainAttributeTypes()
    {
        var json = Entity("""{ "LogicalName": "tn_name", "SchemaName": "tn_Name", "AttributeType": "String" }""");
        Assert.Empty(EntityJsonDefinitionReader.ListOptionBearingAttributes(json));
    }

    // ---- IsGlobal-based local/global detection (round 5 regression - the big one) ----

    private static string PicklistEntity(string optionSetJsonForAttribute) => Entity(
        """{ "LogicalName": "tn_picklist", "SchemaName": "tn_Picklist", "AttributeType": "Picklist" }""");

    private static IReadOnlyDictionary<string, string> OptionSetMap(string json) =>
        new Dictionary<string, string> { ["tn_picklist"] = json };

    [Fact]
    public void Read_LocalOptionSet_WithGlobalOptionSetAlsoPresentButIsGlobalFalse_ReadsAsLocal()
    {
        // Confirmed live: Dataverse can populate BOTH OptionSet and
        // GlobalOptionSet for a genuinely local option set, as literally the
        // same object, both carrying IsGlobal:false. Only IsGlobal:true on
        // GlobalOptionSet means "this is actually shared."
        var optionSetJson = """
            {
              "OptionSet": {
                "IsGlobal": false,
                "Name": "new_tn_test_tn_picklist",
                "Options": [ { "Value": 1, "Label": { "UserLocalizedLabel": { "Label": "Alpha" } } } ]
              },
              "GlobalOptionSet": {
                "IsGlobal": false,
                "Name": "new_tn_test_tn_picklist",
                "Options": [ { "Value": 1, "Label": { "UserLocalizedLabel": { "Label": "Alpha" } } } ]
              }
            }
            """;

        var attribute = Assert.Single(_reader.Read(PicklistEntity(optionSetJson), null, OptionSetMap(optionSetJson)).Attributes);

        Assert.Null(attribute.GlobalOptionSetName);
        Assert.NotNull(attribute.Options);
        Assert.Equal("Alpha", Assert.Single(attribute.Options!).Label);
    }

    [Fact]
    public void Read_GlobalOptionSet_WithIsGlobalTrue_ReadsAsGlobalChoiceBound()
    {
        var optionSetJson = """
            {
              "GlobalOptionSet": { "IsGlobal": true, "Name": "tn_sharedchoice" }
            }
            """;

        var attribute = Assert.Single(_reader.Read(PicklistEntity(optionSetJson), null, OptionSetMap(optionSetJson)).Attributes);

        Assert.Equal("tn_sharedchoice", attribute.GlobalOptionSetName);
        Assert.Null(attribute.Options);
    }

    [Fact]
    public void Read_NoOptionSetJsonSupplied_LeavesOptionsAndGlobalOptionSetNameNull()
    {
        // optionSetJsonByAttribute is null (or missing this attribute) - "not
        // fetched," never an error.
        var attribute = Assert.Single(_reader.Read(PicklistEntity(""), null, null).Attributes);
        Assert.Null(attribute.Options);
        Assert.Null(attribute.GlobalOptionSetName);
    }

    // ---- Boolean option parsing ----

    [Fact]
    public void Read_BooleanOptionSet_DefaultLabels_AreOmitted()
    {
        var json = Entity("""{ "LogicalName": "tn_flag", "SchemaName": "tn_Flag", "AttributeType": "Boolean" }""");
        var optionSetJson = """
            {
              "DefaultValue": false,
              "OptionSet": {
                "TrueOption": { "Label": { "UserLocalizedLabel": { "Label": "True" } } },
                "FalseOption": { "Label": { "UserLocalizedLabel": { "Label": "False" } } }
              }
            }
            """;

        var attribute = Assert.Single(_reader.Read(json, null, new Dictionary<string, string> { ["tn_flag"] = optionSetJson }).Attributes);

        Assert.Null(attribute.DefaultValue);
        Assert.Null(attribute.TrueOptionLabel);
        Assert.Null(attribute.FalseOptionLabel);
    }

    [Fact]
    public void Read_BooleanOptionSet_CustomLabelsAndTrueDefault_AreKept()
    {
        var json = Entity("""{ "LogicalName": "tn_flag", "SchemaName": "tn_Flag", "AttributeType": "Boolean" }""");
        var optionSetJson = """
            {
              "DefaultValue": true,
              "OptionSet": {
                "TrueOption": { "Label": { "UserLocalizedLabel": { "Label": "Yes" } } },
                "FalseOption": { "Label": { "UserLocalizedLabel": { "Label": "No" } } }
              }
            }
            """;

        var attribute = Assert.Single(_reader.Read(json, null, new Dictionary<string, string> { ["tn_flag"] = optionSetJson }).Attributes);

        Assert.True(attribute.DefaultValue);
        Assert.Equal("Yes", attribute.TrueOptionLabel);
        Assert.Equal("No", attribute.FalseOptionLabel);
    }

    // ---- CRLF normalization (round 8 regression) ----

    [Fact]
    public void Read_DescriptionWithCrlf_NormalizesToLf()
    {
        var json = $$"""
            {
              "LogicalName": "tn_test",
              "SchemaName": "tn_Test",
              "OwnershipType": "UserOwned",
              "Description": { "UserLocalizedLabel": { "Label": "Line one.\r\n\r\nLine two." } },
              "Attributes": []
            }
            """;

        var result = _reader.Read(json);

        Assert.Equal("Line one.\n\nLine two.", result.Description);
        Assert.DoesNotContain('\r', result.Description!);
    }

    [Fact]
    public void Read_AttributeDescriptionWithLoneCr_NormalizesToLf()
    {
        var json = Entity("""
            {
              "LogicalName": "tn_x",
              "SchemaName": "tn_X",
              "AttributeType": "String",
              "Description": { "UserLocalizedLabel": { "Label": "One\rTwo" } }
            }
            """);

        var attribute = Assert.Single(_reader.Read(json).Attributes);
        Assert.Equal("One\nTwo", attribute.Description);
    }

    [Fact]
    public void Read_LabelFallsBackToFirstLocalizedLabel_WhenNoUserLocalizedLabel()
    {
        var json = $$"""
            {
              "LogicalName": "tn_test",
              "SchemaName": "tn_Test",
              "OwnershipType": "UserOwned",
              "DisplayName": { "LocalizedLabels": [ { "Label": "Fallback Name", "LanguageCode": 1036 } ] },
              "Attributes": []
            }
            """;

        Assert.Equal("Fallback Name", _reader.Read(json).DisplayName);
    }
}

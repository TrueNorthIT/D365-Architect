using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="AttributeMetadataJsonBuilder"/>, including the two
/// confirmed-live bugs found this session:
/// <list type="bullet">
/// <item>Binding a new Picklist/MultiSelectPicklist to an existing global
/// choice must use the choice's raw MetadataId GUID, never the <c>Name=</c>
/// alternate-key form Dataverse's attribute-create endpoint rejects.</item>
/// <item><see cref="AttributeMetadataJsonBuilder.ApplyUpdateFields"/> must set
/// <c>@odata.type</c> explicitly, or Dataverse silently rejects whichever of
/// a type's own properties don't belong on whatever it falls back to
/// resolving (confirmed for both Boolean and Picklist).</item>
/// </list>
/// </summary>
public sealed class AttributeMetadataJsonBuilderTests
{
    private static AttributeDefinition Attr(string type, string? schemaName = "tn_Test", Action<AttributeDefinitionBuilder>? configure = null) =>
        AttributeDefinitionBuilder.Create(type, schemaName, configure);

    // ---- BuildCreateBody ----

    [Fact]
    public void BuildCreateBody_NoSchemaName_Throws()
    {
        var attribute = Attr("String", schemaName: null);
        Assert.Throws<InvalidOperationException>(() => AttributeMetadataJsonBuilder.BuildCreateBody(attribute));
    }

    [Fact]
    public void BuildCreateBody_UnsupportedType_ThrowsNotSupported()
    {
        var attribute = Attr("Uniqueidentifier");
        Assert.Throws<NotSupportedException>(() => AttributeMetadataJsonBuilder.BuildCreateBody(attribute));
    }

    [Fact]
    public void BuildCreateBody_String_DefaultsMaxLengthAndFormat()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("String"));

        Assert.Equal("Microsoft.Dynamics.CRM.StringAttributeMetadata", (string)body["@odata.type"]!);
        Assert.Equal(100, (int)body["MaxLength"]!);
        Assert.Equal("Text", (string)body["FormatName"]!["Value"]!);
        Assert.Equal("None", (string)body["RequiredLevel"]!["Value"]!);
    }

    [Fact]
    public void BuildCreateBody_Memo_DefaultsMaxLengthAndFormat()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("Memo"));
        Assert.Equal(2000, (int)body["MaxLength"]!);
        Assert.Equal("TextArea", (string)body["Format"]!);
    }

    [Fact]
    public void BuildCreateBody_Integer_DefaultsFullIntRangeAndFormat()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("Integer"));
        Assert.Equal(int.MinValue, (int)body["MinValue"]!);
        Assert.Equal(int.MaxValue, (int)body["MaxValue"]!);
        Assert.Equal("None", (string)body["Format"]!);
    }

    [Fact]
    public void BuildCreateBody_BigInt_HasNoMinMaxValue()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("BigInt"));
        Assert.False(body.ContainsKey("MinValue"));
        Assert.False(body.ContainsKey("MaxValue"));
    }

    [Fact]
    public void BuildCreateBody_Money_DefaultsPrecisionSourceToOrganization()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("Money"));
        Assert.Equal(1, (int)body["PrecisionSource"]!);
    }

    [Fact]
    public void BuildCreateBody_DateTime_DefaultsToDateAndTime()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("DateTime"));
        Assert.Equal("DateAndTime", (string)body["Format"]!);
    }

    [Fact]
    public void BuildCreateBody_Boolean_DefaultsLabelsAndValue()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("Boolean"));

        Assert.False((bool)body["DefaultValue"]!);
        var optionSet = body["OptionSet"]!;
        Assert.Equal("True", (string)optionSet["TrueOption"]!["Label"]!["LocalizedLabels"]![0]!["Label"]!);
        Assert.Equal("False", (string)optionSet["FalseOption"]!["Label"]!["LocalizedLabels"]![0]!["Label"]!);
        Assert.Equal(1, (int)optionSet["TrueOption"]!["Value"]!);
        Assert.Equal(0, (int)optionSet["FalseOption"]!["Value"]!);
    }

    [Fact]
    public void BuildCreateBody_Picklist_WithLocalOptions_BuildsNonGlobalOptionSet()
    {
        var attribute = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        var optionSet = body["OptionSet"]!;
        Assert.False((bool)optionSet["IsGlobal"]!);
        Assert.Equal("Picklist", (string)optionSet["OptionSetType"]!);
        var options = optionSet["Options"]!.AsArray();
        Assert.Single(options);
        Assert.Equal(1, (int)options[0]!["Value"]!);
    }

    [Fact]
    public void BuildCreateBody_MultiSelectPicklist_ForcesVirtualAttributeType_ButNotODataTypeOrTypeName()
    {
        var attribute = Attr("MultiSelectPicklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }]);

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);

        Assert.Equal("Virtual", (string)body["AttributeType"]!);
        Assert.Equal("Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata", (string)body["@odata.type"]!);
        Assert.Equal("MultiSelectPicklistType", (string)body["AttributeTypeName"]!["Value"]!);
    }

    [Fact]
    public void BuildCreateBody_PicklistWithGlobalOptionSetName_NoMetadataIdSupplied_Throws()
    {
        // Regression: this must never fall back to a Name= bind - Dataverse
        // rejects that form for this specific bind target live.
        var attribute = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_shared");

        var ex = Assert.Throws<InvalidOperationException>(() => AttributeMetadataJsonBuilder.BuildCreateBody(attribute, globalOptionSetMetadataId: null));
        Assert.Contains("tn_shared", ex.Message);
    }

    [Fact]
    public void BuildCreateBody_PicklistWithGlobalOptionSetName_BindsByRawMetadataIdGuid()
    {
        var attribute = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_shared");
        var metadataId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute, metadataId);

        Assert.Equal($"/GlobalOptionSetDefinitions({metadataId:D})", (string)body["GlobalOptionSet@odata.bind"]!);
        Assert.False(body.ContainsKey("OptionSet"));
        // Confirmed live: the Name= alternate-key form never appears anywhere in the body.
        Assert.DoesNotContain("Name=", body.ToJsonString());
    }

    [Fact]
    public void BuildCreateBody_OmitsDisplayNameAndDescription_WhenNotSpecified()
    {
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(Attr("String"));
        Assert.False(body.ContainsKey("DisplayName"));
        Assert.False(body.ContainsKey("Description"));
    }

    [Fact]
    public void BuildCreateBody_UsesGivenRequiredLevel_WhenSpecified()
    {
        var attribute = Attr("String", configure: b => b.RequiredLevel = "ApplicationRequired");
        var body = AttributeMetadataJsonBuilder.BuildCreateBody(attribute);
        Assert.Equal("ApplicationRequired", (string)body["RequiredLevel"]!["Value"]!);
    }

    // ---- ApplyUpdateFields (round 7 regression: @odata.type must always be set) ----

    [Fact]
    public void ApplyUpdateFields_AlwaysSetsODataTypeExplicitly()
    {
        foreach (var type in AttributeMetadataJsonBuilder.SupportedTypes)
        {
            var existing = new System.Text.Json.Nodes.JsonObject();
            AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, Attr(type));

            Assert.Equal($"Microsoft.Dynamics.CRM.{type}AttributeMetadata", (string)existing["@odata.type"]!);
        }
    }

    [Fact]
    public void ApplyUpdateFields_UnsupportedType_Throws()
    {
        var existing = new System.Text.Json.Nodes.JsonObject();
        Assert.Throws<NotSupportedException>(() => AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, Attr("Uniqueidentifier")));
    }

    [Fact]
    public void ApplyUpdateFields_NullFields_LeavesExistingValuesAlone()
    {
        var existing = new System.Text.Json.Nodes.JsonObject { ["DisplayName"] = "should stay", ["MaxLength"] = 500 };
        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, Attr("String"));

        Assert.Equal("should stay", (string)existing["DisplayName"]!);
        Assert.Equal(500, (int)existing["MaxLength"]!);
    }

    [Fact]
    public void ApplyUpdateFields_SetsSpecifiedDisplayNameDescriptionAndRequiredLevel()
    {
        var existing = new System.Text.Json.Nodes.JsonObject();
        var attribute = Attr("String", configure: b =>
        {
            b.DisplayName = "New Name";
            b.Description = "New Description";
            b.RequiredLevel = "SystemRequired";
        });

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, attribute);

        Assert.Equal("New Name", (string)existing["DisplayName"]!["LocalizedLabels"]![0]!["Label"]!);
        Assert.Equal("New Description", (string)existing["Description"]!["LocalizedLabels"]![0]!["Label"]!);
        Assert.Equal("SystemRequired", (string)existing["RequiredLevel"]!["Value"]!);
    }

    [Fact]
    public void ApplyUpdateFields_Picklist_NeverSetsOptionSet()
    {
        // Options always go through BuildOptionChangePlans instead - never the plain attribute PUT.
        var existing = new System.Text.Json.Nodes.JsonObject();
        var attribute = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }]);

        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, attribute);

        Assert.False(existing.ContainsKey("OptionSet"));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Lookup")]
    [InlineData("Customer")]
    [InlineData("State")]
    [InlineData("Status")]
    public void ApplyUpdateFields_ImmutableTypes_OnlyTouchSharedFields(string type)
    {
        var existing = new System.Text.Json.Nodes.JsonObject();
        AttributeMetadataJsonBuilder.ApplyUpdateFields(existing, Attr(type, configure: b => b.DisplayName = "Renamed"));

        Assert.Equal("Renamed", (string)existing["DisplayName"]!["LocalizedLabels"]![0]!["Label"]!);
        // Nothing type-specific was ever added beyond @odata.type/DisplayName.
        Assert.Equal(2, existing.Count);
    }

    // ---- BuildRelationshipCreateBody (Lookup) ----

    [Fact]
    public void BuildRelationshipCreateBody_MissingRelationshipSchemaName_Throws()
    {
        var attribute = Attr("Lookup", configure: b => b.Targets = ["contact"]);
        Assert.Throws<InvalidOperationException>(() => AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute));
    }

    [Fact]
    public void BuildRelationshipCreateBody_NoTargets_Throws()
    {
        var attribute = Attr("Lookup", configure: b => b.RelationshipSchemaName = "tn_test_contact");
        Assert.Throws<InvalidOperationException>(() => AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute));
    }

    [Fact]
    public void BuildRelationshipCreateBody_MultipleTargets_Throws()
    {
        var attribute = Attr("Lookup", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_multi";
            b.Targets = ["contact", "account"];
        });
        Assert.Throws<InvalidOperationException>(() => AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute));
    }

    [Fact]
    public void BuildRelationshipCreateBody_HappyPath_DerivesReferencedAttributeFromTargetPlusId()
    {
        var attribute = Attr("Lookup", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
        });

        var body = AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute);

        Assert.Equal("tn_test_contact", (string)body["SchemaName"]!);
        Assert.Equal("contactid", (string)body["ReferencedAttribute"]!);
        Assert.Equal("contact", (string)body["ReferencedEntity"]!);
        Assert.Equal("tn_test", (string)body["ReferencingEntity"]!);
        Assert.Equal("tn_Test", (string)body["Lookup"]!["SchemaName"]!);
        Assert.Equal("Microsoft.Dynamics.CRM.LookupAttributeMetadata", (string)body["Lookup"]!["@odata.type"]!);
    }

    [Fact]
    public void BuildRelationshipCreateBody_UsesReferentialCascadeBehavior_NotParental()
    {
        // Parental (all-Cascade) would block creating this lookup outright
        // if the entity already has a Parental relationship to a different
        // parent — Dataverse only allows one. New lookups must default to
        // Referential, matching the Maker UI, not Parental.
        var attribute = Attr("Lookup", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
        });

        var body = AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute);

        var cascade = body["CascadeConfiguration"]!;
        Assert.Equal("NoCascade", (string)cascade["Assign"]!);
        Assert.Equal("RemoveLink", (string)cascade["Delete"]!);
        Assert.Equal("NoCascade", (string)cascade["Merge"]!);
        Assert.Equal("NoCascade", (string)cascade["Reparent"]!);
        Assert.Equal("NoCascade", (string)cascade["Share"]!);
        Assert.Equal("NoCascade", (string)cascade["Unshare"]!);
    }

    [Theory]
    [InlineData("Referential")]
    [InlineData("ReferentialRestrictDelete")]
    public void BuildRelationshipCreateBody_NonParentalBehaviors_NeverTripParentalDeterminant(string behavior)
    {
        // Regression test for the exact bug this tool shipped once already:
        // Microsoft's own documented rule (entity-relationship-behavior
        // #BKMK_ParentalEntityRelationships) is that a relationship counts
        // as Parental — and so collides with an existing Parental
        // relationship on the same entity — if Delete=Cascade, OR any of
        // Assign/Share/Unshare/Reparent is Cascade/UserOwned/Active. An
        // earlier "Referential" preset here set Reparent: Cascade and still
        // hit 0x80047007 despite being named Referential. Confirmed live.
        var attribute = Attr("Lookup", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
            b.RelationshipBehavior = behavior;
        });

        var body = AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute);
        var cascade = body["CascadeConfiguration"]!;

        Assert.NotEqual("Cascade", (string)cascade["Delete"]!);
        foreach (var action in new[] { "Assign", "Share", "Unshare", "Reparent" })
        {
            Assert.Equal("NoCascade", (string)cascade[action]!);
        }
    }

    [Theory]
    [InlineData("Parental", "Cascade", "Cascade", "Cascade", "Cascade", "Cascade", "Cascade")]
    [InlineData("ReferentialRestrictDelete", "NoCascade", "Restrict", "NoCascade", "NoCascade", "NoCascade", "NoCascade")]
    [InlineData("referential", "NoCascade", "RemoveLink", "NoCascade", "NoCascade", "NoCascade", "NoCascade")]
    public void BuildRelationshipCreateBody_RelationshipBehavior_ChoosesMatchingCascadeConfiguration(
        string behavior, string assign, string delete, string merge, string reparent, string share, string unshare)
    {
        var attribute = Attr("Lookup", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
            b.RelationshipBehavior = behavior;
        });

        var body = AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute);

        var cascade = body["CascadeConfiguration"]!;
        Assert.Equal(assign, (string)cascade["Assign"]!);
        Assert.Equal(delete, (string)cascade["Delete"]!);
        Assert.Equal(merge, (string)cascade["Merge"]!);
        Assert.Equal(reparent, (string)cascade["Reparent"]!);
        Assert.Equal(share, (string)cascade["Share"]!);
        Assert.Equal(unshare, (string)cascade["Unshare"]!);
    }

    [Fact]
    public void BuildRelationshipCreateBody_InvalidRelationshipBehavior_Throws()
    {
        var attribute = Attr("Lookup", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
            b.RelationshipBehavior = "NotARealBehavior";
        });

        Assert.Throws<InvalidOperationException>(() => AttributeMetadataJsonBuilder.BuildRelationshipCreateBody("tn_test", attribute));
    }

    // ---- BuildCustomerRelationshipCreateBody (Customer) ----

    [Fact]
    public void BuildCustomerRelationshipCreateBody_MissingSchemaName_Throws()
    {
        var attribute = Attr("Customer", schemaName: null);
        Assert.Throws<InvalidOperationException>(() => AttributeMetadataJsonBuilder.BuildCustomerRelationshipCreateBody("tn_test", attribute));
    }

    [Fact]
    public void BuildCustomerRelationshipCreateBody_DerivesBothRelationshipNamesFromSchemaName()
    {
        var attribute = Attr("Customer", schemaName: "tn_Customer");

        var body = AttributeMetadataJsonBuilder.BuildCustomerRelationshipCreateBody("tn_test", attribute);

        var relationships = body["OneToManyRelationships"]!.AsArray();
        Assert.Equal(2, relationships.Count);
        Assert.Contains(relationships, r => (string)r!["SchemaName"]! == "tn_Customer_account" && (string)r["ReferencedEntity"]! == "account");
        Assert.Contains(relationships, r => (string)r!["SchemaName"]! == "tn_Customer_contact" && (string)r["ReferencedEntity"]! == "contact");
        Assert.Equal("Microsoft.Dynamics.CRM.ComplexLookupAttributeMetadata", (string)body["Lookup"]!["@odata.type"]!);
        // Confirmed against Microsoft's own example: Customer's own lookup never carries RequiredLevel.
        Assert.False(body["Lookup"]!.AsObject().ContainsKey("RequiredLevel"));
    }

    // ---- BuildOptionChangePlans ----

    [Fact]
    public void BuildOptionChangePlans_Boolean_RenamesOnlyChangedLabels()
    {
        var local = Attr("Boolean", configure: b => { b.TrueOptionLabel = "Yes"; b.FalseOptionLabel = "No"; });
        var existing = Attr("Boolean", configure: b => { b.TrueOptionLabel = "Yes"; b.FalseOptionLabel = "False"; });

        var plans = AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing);

        // True unchanged ("Yes" == "Yes") - no plan; False changed ("No" != "False") - one plan.
        var plan = Assert.Single(plans);
        Assert.Equal(OptionChangeAction.UpdateOption, plan.Action);
        Assert.Equal(0, (int)plan.RequestBody["Value"]!);
    }

    [Fact]
    public void BuildOptionChangePlans_LocalPicklist_DelegatesToOptionSetDiffer()
    {
        var local = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Red" }, new AttributeOptionDefinition { Value = 2, Label = "Blue" }]);
        var existing = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);

        var plans = AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing);

        var plan = Assert.Single(plans);
        Assert.Equal(OptionChangeAction.InsertOption, plan.Action);
    }

    [Fact]
    public void BuildOptionChangePlans_GlobalBoundPicklist_ProducesNoPlans()
    {
        // Options on a globally-bound column are never diffed here at all -
        // that's choice import's job, via GlobalChoiceMetadataJsonBuilder.
        var local = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_shared");
        var existing = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_shared");

        Assert.Empty(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing));
    }

    [Fact]
    public void BuildOptionChangePlans_State_GlobalBound_ProducesNoPlans()
    {
        var local = Attr("State", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Renamed" }]);
        var existing = Attr("State", configure: b =>
        {
            b.GlobalOptionSetName = "tn_test_statecode";
            b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Original" }];
        });

        Assert.Empty(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing));
    }

    [Fact]
    public void BuildOptionChangePlans_State_LocalBound_RenamesMatchedValue()
    {
        var local = Attr("State", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Renamed" }]);
        var existing = Attr("State", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Original" }]);

        var plan = Assert.Single(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing));

        Assert.Equal(OptionChangeAction.UpdateStateValue, plan.Action);
    }

    [Fact]
    public void BuildOptionChangePlans_Status_MatchByValue_Renames()
    {
        var local = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "New Label" }]);
        var existing = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Old Label" }]);

        var plan = Assert.Single(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing));

        Assert.Equal(OptionChangeAction.UpdateOption, plan.Action);
    }

    [Fact]
    public void BuildOptionChangePlans_Status_UnmatchedValueButMatchedLabel_IsNotInsertedAgain()
    {
        // A status this tool already inserted, whose YAML hasn't been
        // re-exported to pick up the real assigned Value yet.
        var local = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 999999, Label = "Already Live" }]);
        var existing = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Already Live" }]);

        Assert.Empty(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing));
    }

    [Fact]
    public void BuildOptionChangePlans_Status_UnmatchedWithState_InsertsNewStatus()
    {
        var local = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 999999, Label = "Brand New", State = 1 }]);
        var existing = Attr("Status", configure: b => b.Options = []);

        var plan = Assert.Single(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing));

        Assert.Equal(OptionChangeAction.InsertStatusValue, plan.Action);
        Assert.Equal(1, (int)plan.RequestBody["StateCode"]!);
    }

    [Fact]
    public void BuildOptionChangePlans_Status_UnmatchedWithNoState_NeverInserted()
    {
        var local = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 999999, Label = "Brand New" }]);
        var existing = Attr("Status", configure: b => b.Options = []);

        Assert.Empty(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", local, existing));
    }

    [Fact]
    public void BuildOptionChangePlans_UnrelatedType_ProducesNoPlans()
    {
        Assert.Empty(AttributeMetadataJsonBuilder.BuildOptionChangePlans("tn_test", Attr("String"), Attr("String")));
    }
}

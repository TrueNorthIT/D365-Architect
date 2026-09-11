using System.Text.Json.Nodes;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="GlobalChoiceMetadataJsonBuilder"/>, including the two
/// confirmed-live fixes to <see cref="GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields"/>:
/// stripping <c>Options</c> (400 "Invalid property 'Options'" otherwise) and
/// setting <c>@odata.type</c> explicitly (500 "Cannot create an abstract
/// class" otherwise).
/// </summary>
public sealed class GlobalChoiceMetadataJsonBuilderTests
{
    private static GlobalChoiceDefinition Choice(string name = "tn_test", string? displayName = null, string? description = null, IReadOnlyList<AttributeOptionDefinition>? options = null) => new()
    {
        Name = name,
        DisplayName = displayName,
        Description = description,
        Options = options,
    };

    [Fact]
    public void BuildCreateBody_SetsNameOptionSetTypeAndOptions()
    {
        var choice = Choice(options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);

        var body = GlobalChoiceMetadataJsonBuilder.BuildCreateBody(choice);

        Assert.Equal("tn_test", (string)body["Name"]!);
        Assert.Equal("Picklist", (string)body["OptionSetType"]!);
        Assert.Equal("Microsoft.Dynamics.CRM.OptionSetMetadata", (string)body["@odata.type"]!);
        var options = body["Options"]!.AsArray();
        Assert.Single(options);
        Assert.Equal(1, (int)options[0]!["Value"]!);
    }

    [Fact]
    public void BuildCreateBody_OmitsDisplayNameAndDescription_WhenNotSpecified()
    {
        var choice = Choice(options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);
        var body = GlobalChoiceMetadataJsonBuilder.BuildCreateBody(choice);

        Assert.False(body.ContainsKey("DisplayName"));
        Assert.False(body.ContainsKey("Description"));
    }

    [Fact]
    public void BuildCreateBody_IncludesDisplayNameAndDescription_WhenSpecified()
    {
        var choice = Choice(displayName: "Colors", description: "A choice of colors", options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);
        var body = GlobalChoiceMetadataJsonBuilder.BuildCreateBody(choice);

        Assert.Equal("Colors", (string)body["DisplayName"]!["LocalizedLabels"]![0]!["Label"]!);
        Assert.Equal("A choice of colors", (string)body["Description"]!["LocalizedLabels"]![0]!["Label"]!);
    }

    // ---- ApplyUpdateFields (rounds 4/5 regression) ----

    [Fact]
    public void ApplyUpdateFields_StripsOptionsFromExisting()
    {
        var existing = new JsonObject
        {
            ["Options"] = new JsonArray(new JsonObject { ["Value"] = 1, ["Label"] = "Red" }),
            ["MetadataId"] = Guid.NewGuid().ToString(),
        };

        GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields(existing, Choice());

        Assert.False(existing.ContainsKey("Options"));
    }

    [Fact]
    public void ApplyUpdateFields_SetsODataTypeExplicitly()
    {
        var existing = new JsonObject();
        GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields(existing, Choice());

        Assert.Equal("Microsoft.Dynamics.CRM.OptionSetMetadata", (string)existing["@odata.type"]!);
    }

    [Fact]
    public void ApplyUpdateFields_OverwritesAnyPreexistingODataType()
    {
        // The clone came back with whatever (or no) @odata.type the GET
        // response happened to carry - this must always win.
        var existing = new JsonObject { ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadataBase" };
        GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields(existing, Choice());

        Assert.Equal("Microsoft.Dynamics.CRM.OptionSetMetadata", (string)existing["@odata.type"]!);
    }

    [Fact]
    public void ApplyUpdateFields_NullDisplayNameAndDescription_LeaveExistingValuesAlone()
    {
        var existing = new JsonObject { ["DisplayName"] = "keep me" };
        GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields(existing, Choice());

        Assert.Equal("keep me", (string)existing["DisplayName"]!);
        Assert.False(existing.ContainsKey("Description"));
    }

    [Fact]
    public void ApplyUpdateFields_SetsSpecifiedDisplayNameAndDescription()
    {
        var existing = new JsonObject();
        GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields(existing, Choice(displayName: "New Name", description: "New Description"));

        Assert.Equal("New Name", (string)existing["DisplayName"]!["LocalizedLabels"]![0]!["Label"]!);
        Assert.Equal("New Description", (string)existing["Description"]!["LocalizedLabels"]![0]!["Label"]!);
    }

    // ---- BuildOptionChangePlans ----

    [Fact]
    public void BuildOptionChangePlans_LocalOptionsNull_ProducesNoPlans()
    {
        var local = Choice(options: null);
        var existing = Choice(options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);

        Assert.Empty(GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans(local, existing));
    }

    [Fact]
    public void BuildOptionChangePlans_NewOption_ProducesInsertAddressedByOptionSetName()
    {
        var local = Choice(name: "tn_colors", options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }, new AttributeOptionDefinition { Value = 2, Label = "Blue" }]);
        var existing = Choice(name: "tn_colors", options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);

        var plan = Assert.Single(GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans(local, existing));

        Assert.Equal(OptionChangeAction.InsertOption, plan.Action);
        Assert.Equal("tn_colors", (string)plan.RequestBody["OptionSetName"]!);
        // Global choice option bodies are addressed by OptionSetName - never AttributeLogicalName/EntityLogicalName.
        Assert.False(plan.RequestBody.ContainsKey("AttributeLogicalName"));
        Assert.False(plan.RequestBody.ContainsKey("EntityLogicalName"));
    }

    [Fact]
    public void BuildOptionChangePlans_RenamedOption_ProducesUpdateWithMergeLabels()
    {
        var local = Choice(options: [new AttributeOptionDefinition { Value = 1, Label = "Crimson" }]);
        var existing = Choice(options: [new AttributeOptionDefinition { Value = 1, Label = "Red" }]);

        var plan = Assert.Single(GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans(local, existing));

        Assert.Equal(OptionChangeAction.UpdateOption, plan.Action);
        Assert.True((bool)plan.RequestBody["MergeLabels"]!);
    }
}

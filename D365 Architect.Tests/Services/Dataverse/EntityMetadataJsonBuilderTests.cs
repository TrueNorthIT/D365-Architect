using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="EntityMetadataJsonBuilder.BuildCreateBody"/> - the body
/// of <c>POST EntityDefinitions</c> that creates a table.
///
/// Body shape is taken from Microsoft Learn, "Create table definitions":
/// https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api
/// - an <c>EntityMetadata</c> with <c>SchemaName</c>, <c>DisplayName</c>,
/// <c>DisplayCollectionName</c>, <c>Description</c>, <c>OwnershipType</c>,
/// <c>IsActivity</c>, <c>HasActivities</c>, <c>HasNotes</c>, and an
/// <c>Attributes</c> array holding exactly one <c>StringAttributeMetadata</c>
/// with <c>IsPrimaryName: true</c> (and <c>FormatName</c> <c>Text</c>,
/// <c>MaxLength</c>, <c>RequiredLevel</c>, <c>DisplayName</c>). Labels use the
/// same <c>Label</c>/<c>LocalizedLabel</c> shape as <see cref="DataverseLabelJson"/>.
/// Only properties that page shows are asserted here.
/// </summary>
public sealed class EntityMetadataJsonBuilderTests
{
    private static JsonObject Build(EntityDefinition? entity = null) => EntityMetadataJsonBuilder.BuildCreateBody(entity ?? TableCreateFixtures.Table());

    private static string LabelText(JsonNode? label) => (string)label!["LocalizedLabels"]![0]!["Label"]!;

    // Learn: the table is an EntityMetadata carrying the required table properties.
    [Fact]
    public void BuildCreateBody_DescribesTheTableAsEntityMetadata()
    {
        var body = Build();

        Expect.Equal("Microsoft.Dynamics.CRM.EntityMetadata", (string?)body["@odata.type"], "the create body should be a Microsoft.Dynamics.CRM.EntityMetadata.");
        Expect.Equal("tn_Project", (string?)body["SchemaName"], "SchemaName should come from the YAML's schemaName.");
        Expect.Equal("UserOwned", (string?)body["OwnershipType"], "OwnershipType should come from the YAML's ownershipType.");
        Expect.Equal("Project", LabelText(body["DisplayName"]), "DisplayName should carry the YAML's displayName as a label.");
        Expect.Equal("Projects", LabelText(body["DisplayCollectionName"]), "DisplayCollectionName should carry the YAML's pluralDisplayName as a label.");
        Expect.Equal("Tracks projects", LabelText(body["Description"]), "Description should carry the YAML's description as a label.");
    }

    // OrganizationOwned is the other supported ownership, passed through unchanged.
    [Fact]
    public void BuildCreateBody_OrganizationOwned_IsPassedThrough()
    {
        var body = Build(TableCreateFixtures.Table(ownershipType: "OrganizationOwned"));

        Expect.Equal("OrganizationOwned", (string?)body["OwnershipType"], "OrganizationOwned should be sent as OwnershipType.");
    }

    // Learn lists IsActivity/HasActivities/HasNotes as required properties, so they are sent explicitly - false when the YAML is silent
    // (silence means Dataverse's default, which is false; see docs/yaml-conventions.md).
    [Fact]
    public void BuildCreateBody_FlagsTheYamlLeavesOut_AreSentAsFalse()
    {
        var body = Build();

        Expect.Equal(false, (bool?)body["IsActivity"], "IsActivity is a required property and should be sent as false.");
        Expect.Equal(false, (bool?)body["HasActivities"], "HasActivities is a required property and should be sent as false when the YAML omits it.");
        Expect.Equal(false, (bool?)body["HasNotes"], "HasNotes is a required property and should be sent as false when the YAML omits it.");
    }

    [Fact]
    public void BuildCreateBody_HasActivitiesAndHasNotesTrue_AreSentAsTrue()
    {
        var body = Build(TableCreateFixtures.Table(hasActivities: true, hasNotes: true));

        Expect.Equal(true, (bool?)body["HasActivities"], "'hasActivities: true' should be sent as HasActivities true.");
        Expect.Equal(true, (bool?)body["HasNotes"], "'hasNotes: true' should be sent as HasNotes true.");
    }

    // "Creation makes only the table and its primary name column; other columns in the file are left for `table import`."
    // Example: file lists tn_name, tn_budget, tn_startdate -> only the primary name column is in the body's Attributes.
    [Fact]
    public void BuildCreateBody_FileWithThreeColumns_CarriesOnlyThePrimaryNameColumn()
    {
        var body = Build();

        var attributes = body["Attributes"]!.AsArray();
        var only = Assert.Single(attributes);
        Expect.Equal("tn_Name", (string?)only!["SchemaName"], "the only column in the create body should be the primary name column (tn_name), not tn_budget or tn_startdate.");
    }

    // The primary name column is a StringAttributeMetadata flagged IsPrimaryName, with Text format (Learn).
    [Fact]
    public void BuildCreateBody_PrimaryNameColumn_IsAPrimaryNameStringAttribute()
    {
        var primary = Assert.Single(Build()["Attributes"]!.AsArray())!;

        Expect.Equal("Microsoft.Dynamics.CRM.StringAttributeMetadata", (string?)primary["@odata.type"], "the primary name column should be a StringAttributeMetadata.");
        Expect.Equal("String", (string?)primary["AttributeType"], "the primary name column's AttributeType should be String.");
        Expect.Equal(true, (bool?)primary["IsPrimaryName"], "the primary name column should be flagged IsPrimaryName: true.");
        Expect.Equal("Text", (string?)primary["FormatName"]!["Value"], "the primary name column must use Text format.");
        Expect.Equal("Name", LabelText(primary["DisplayName"]), "the primary name column's DisplayName should come from the YAML column.");
    }

    // MaxLength follows the YAML column when given, otherwise the column builder's own default (100, as in Learn's example).
    [Fact]
    public void BuildCreateBody_PrimaryNameColumnMaxLength_FollowsTheYamlColumn()
    {
        var body = Build(TableCreateFixtures.Table(attributes: [TableCreateFixtures.NameColumn(maxLength: 200)]));

        var primary = Assert.Single(body["Attributes"]!.AsArray())!;
        Expect.Equal(200, (int?)primary["MaxLength"], "maxLength: 200 on the primary name column should be sent as MaxLength 200.");
    }

    // The column named by primaryNameAttribute is the one sent, wherever it sits in the file.
    [Fact]
    public void BuildCreateBody_PrimaryNameColumnListedSecond_IsStillTheOneSent()
    {
        var other = new AttributeDefinition { Name = "tn_code", Type = "String", SchemaName = "tn_Code" };
        var body = Build(TableCreateFixtures.Table(attributes: [other, TableCreateFixtures.NameColumn()]));

        var primary = Assert.Single(body["Attributes"]!.AsArray())!;
        Expect.Equal("tn_Name", (string?)primary["SchemaName"], "the primary name column should be the one primaryNameAttribute names (tn_name), not the first column listed.");
    }

    // An activity table is refused, so no body is ever built for it.
    [Fact]
    public void BuildCreateBody_ActivityTable_IsRefused()
    {
        var ex = Assert.Throws<TableCreateValidationException>(() => Build(TableCreateFixtures.Table(isActivity: true)));

        Expect.Contains("Activity tables aren't supported by table create yet", ex.Message, "building a body for an activity table should be refused with the activity-tables message.");
    }

    // An unsupported ownership is refused, never mapped to something else.
    [Fact]
    public void BuildCreateBody_TeamOwned_IsRefused()
    {
        var ex = Assert.Throws<TableCreateValidationException>(() => Build(TableCreateFixtures.Table(ownershipType: "TeamOwned")));

        Expect.Contains("TeamOwned", ex.Message, "building a body for a TeamOwned table should be refused, naming the ownership.");
    }

    // "never guessed" - a missing ownershipType is an error, not a defaulted UserOwned.
    [Fact]
    public void BuildCreateBody_MissingOwnershipType_IsRefusedNotDefaulted()
    {
        var ex = Assert.Throws<TableCreateValidationException>(() => Build(TableCreateFixtures.Table(ownershipType: null)));

        Expect.Contains("ownershipType is required to create tn_project", ex.Message, "a missing ownershipType should stop the build with 'ownershipType is required to create tn_project'.");
    }
}

using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="TableCreateValidator.Validate"/> - what <c>table create</c>
/// refuses before anything is written. It returns every
/// problem found, so one run lists everything to fix. Message wording follows
/// the requirement examples: "ownershipType is required to create tn_project",
/// "Activity tables aren't supported by table create yet".
/// </summary>
public sealed class TableCreateValidatorTests
{
    private static string Joined(EntityDefinition entity) => string.Join(" | ", TableCreateValidator.Validate(entity));

    // "Creation is refused unless `schemaName` (with publisher prefix), `ownershipType`,
    // `primaryNameAttribute` and both display names are given - never guessed."
    // A table with all of them (and a String primary name column) has no problems.
    [Fact]
    public void Validate_FullyDescribedTable_HasNoProblems()
    {
        var problems = TableCreateValidator.Validate(TableCreateFixtures.Table());

        Expect.Empty(problems, "a table with schemaName, ownershipType, primaryNameAttribute, displayName and pluralDisplayName should be creatable.");
    }

    // Example: Missing `ownershipType` -> stops with "ownershipType is required to create tn_project".
    // Each required key is checked on its own; the message is "<yaml key> is required to create <logical name>".
    [Theory]
    [InlineData("schemaName")]
    [InlineData("ownershipType")]
    [InlineData("primaryNameAttribute")]
    [InlineData("displayName")]
    [InlineData("pluralDisplayName")]
    public void Validate_RequiredKeyMissing_SaysItIsRequiredToCreateTheTable(string missingKey)
    {
        var entity = TableCreateFixtures.Table(
            schemaName: missingKey == "schemaName" ? null : "tn_Project",
            ownershipType: missingKey == "ownershipType" ? null : "UserOwned",
            primaryNameAttribute: missingKey == "primaryNameAttribute" ? null : "tn_name",
            displayName: missingKey == "displayName" ? null : "Project",
            pluralDisplayName: missingKey == "pluralDisplayName" ? null : "Projects");

        Expect.Contains($"{missingKey} is required to create tn_project", Joined(entity), $"a table with no {missingKey} should be refused, naming the key and the table.");
    }

    // "never guessed" - a blank value is as good as missing, not a value to default.
    [Fact]
    public void Validate_BlankOwnershipType_IsTreatedAsMissing()
    {
        var problems = Joined(TableCreateFixtures.Table(ownershipType: "   "));

        Expect.Contains("ownershipType is required to create tn_project", problems, "a whitespace-only ownershipType should be refused like a missing one.");
    }

    // "schemaName (with publisher prefix)". A schema name with no prefix is refused.
    // Code path: TableCreateValidator.Validate, reusing DataverseSchemaNaming.SchemaNamePattern.
    [Fact]
    public void Validate_SchemaNameWithoutPublisherPrefix_IsRefused()
    {
        var problems = Joined(TableCreateFixtures.Table(schemaName: "Project"));

        Expect.Contains("schemaName", problems, "a schemaName with no publisher prefix ('Project') should be refused, naming schemaName.");
        Expect.Contains("prefix", problems, "the refusal should say a publisher prefix is needed.");
    }

    // Every problem is reported in one pass, not just the first.
    [Fact]
    public void Validate_SeveralKeysMissing_ReportsEveryOne()
    {
        var problems = TableCreateValidator.Validate(TableCreateFixtures.Table(ownershipType: null, displayName: null));

        Expect.Contains("ownershipType is required to create tn_project", string.Join(" | ", problems), "the missing ownershipType should be reported.");
        Expect.Contains("displayName is required to create tn_project", string.Join(" | ", problems), "the missing displayName should be reported in the same pass.");
    }

    // "Only `UserOwned` or `OrganizationOwned` ownership is supported" - both are accepted.
    [Theory]
    [InlineData("UserOwned")]
    [InlineData("OrganizationOwned")]
    public void Validate_SupportedOwnership_HasNoProblems(string ownershipType)
    {
        var problems = TableCreateValidator.Validate(TableCreateFixtures.Table(ownershipType: ownershipType));

        Expect.Empty(problems, $"ownershipType '{ownershipType}' is supported and should be accepted.");
    }

    // "anything else is refused" - the refusal names the value and the two supported ones.
    // Values are Dataverse's OwnershipTypes: https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/ownershiptypes
    [Theory]
    [InlineData("TeamOwned")]
    [InlineData("BusinessOwned")]
    [InlineData("BusinessParented")]
    [InlineData("None")]
    public void Validate_UnsupportedOwnership_IsRefusedNamingTheSupportedOnes(string ownershipType)
    {
        var problems = Joined(TableCreateFixtures.Table(ownershipType: ownershipType));

        Expect.Contains(ownershipType, problems, $"the refusal should name the unsupported value '{ownershipType}'.");
        Expect.Contains("UserOwned", problems, "the refusal should say UserOwned is supported.");
        Expect.Contains("OrganizationOwned", problems, "the refusal should say OrganizationOwned is supported.");
    }

    // Example: `isActivity: true` -> "Activity tables aren't supported by table create yet".
    [Fact]
    public void Validate_ActivityTable_SaysActivityTablesArentSupportedYet()
    {
        var problems = Joined(TableCreateFixtures.Table(isActivity: true));

        Expect.Contains("Activity tables aren't supported by table create yet", problems, "isActivity: true should be refused with the activity-tables message.");
    }

    // "that column must be ... listed under `attributes`".
    [Fact]
    public void Validate_PrimaryNameAttributeNotListedUnderAttributes_IsRefused()
    {
        var problems = Joined(TableCreateFixtures.Table(primaryNameAttribute: "tn_missing"));

        Expect.Contains("primaryNameAttribute", problems, "a primary name column that isn't in the file should be refused, naming primaryNameAttribute.");
        Expect.Contains("tn_missing", problems, "the refusal should name the column that wasn't found.");
        Expect.Contains("attributes", problems, "the refusal should say it has to be listed under attributes.");
    }

    // "that column must be a String".
    [Fact]
    public void Validate_PrimaryNameAttributeThatIsNotAString_IsRefused()
    {
        var problems = Joined(TableCreateFixtures.Table(primaryNameAttribute: "tn_budget"));

        Expect.Contains("tn_budget", problems, "a Money column named as the primary name column should be refused, naming it.");
        Expect.Contains("String", problems, "the refusal should say the primary name column must be a String.");
    }

    // Microsoft Learn - "The primary name attribute must use Text format"
    // (https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api).
    [Fact]
    public void Validate_PrimaryNameColumnWithANonTextFormat_IsRefused()
    {
        var entity = TableCreateFixtures.Table(attributes: [TableCreateFixtures.NameColumn(format: "Email")]);

        var problems = Joined(entity);

        Expect.Contains("Text", problems, "a primary name column with Format: Email should be refused - Dataverse requires Text format.");
    }

    // "Never guessed": the create body needs the primary column's schema name, and BuildCreateBody never invents one.
    [Fact]
    public void Validate_PrimaryNameColumnWithoutSchemaName_IsRefused()
    {
        var entity = TableCreateFixtures.Table(attributes: [TableCreateFixtures.NameColumn(schemaName: null)]);

        var problems = Joined(entity);

        Expect.Contains("tn_name", problems, "a primary name column with no schemaName should be refused, naming the column.");
        Expect.Contains("schemaName", problems, "the refusal should say schemaName is missing.");
    }
}

using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// The table-level YAML key <c>primaryNameAttribute</c> (REQ-3) and its
/// export side (REQ-9): <see cref="EntityDefinition.PrimaryNameAttribute"/>,
/// <see cref="EntityJsonDefinitionReader"/> filling it from the live table's
/// <c>PrimaryNameAttribute</c>, and <see cref="TableExportService"/> writing
/// it, so export -> rebuild round-trips.
///
/// Two tests here (the plain YAML read and write) already pass once the
/// stubbed property exists - they pin the key's spelling and aren't expected
/// to turn red. The rest fail until the reader, exporter and
/// <see cref="EntityDefinition.WithAttributes"/> carry the value.
/// </summary>
public sealed class PrimaryNameAttributeYamlTests
{
    private static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    // Live shape of the properties involved: EntityDefinitions(...) returns the
    // table's PrimaryNameAttribute alongside its Attributes - see
    // https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api
    // (the Update example's response carries "PrimaryNameAttribute": "new_accountname").
    private const string LiveAccountJson = """
        {
          "LogicalName": "account",
          "SchemaName": "Account",
          "DisplayName": { "UserLocalizedLabel": { "Label": "Account", "LanguageCode": 1033 } },
          "DisplayCollectionName": { "UserLocalizedLabel": { "Label": "Accounts", "LanguageCode": 1033 } },
          "OwnershipType": "UserOwned",
          "PrimaryNameAttribute": "name",
          "Attributes": [
            {
              "LogicalName": "name",
              "SchemaName": "Name",
              "AttributeType": "String",
              "MaxLength": 160,
              "DisplayName": { "UserLocalizedLabel": { "Label": "Account Name", "LanguageCode": 1033 } },
              "RequiredLevel": { "Value": "ApplicationRequired" }
            }
          ]
        }
        """;

    // REQ-3: "The YAML names the primary name column with a table-level `primaryNameAttribute` key"
    // Example: `primaryNameAttribute: tn_name` with `tn_name` as a String column -> accepted.
    // Code path: EntityYamlDeserializer.FromYaml / EntityDefinition.PrimaryNameAttribute.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public void FromYaml_PrimaryNameAttributeKey_PopulatesThePrimaryNameAttribute()
    {
        var entity = EntityYamlDeserializer.FromYaml(TableCreateFixtures.TableYaml());

        Expect.Equal("tn_name", entity.PrimaryNameAttribute, "REQ-3: the table-level 'primaryNameAttribute' key should populate EntityDefinition.PrimaryNameAttribute.");
    }

    // REQ-3 / REQ-9: the key is spelled exactly `primaryNameAttribute` when written.
    // Code path: EntityYamlSerializer.ToYaml.
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public void ToYaml_PrimaryNameAttribute_IsWrittenUnderThePrimaryNameAttributeKey()
    {
        var yaml = EntityYamlSerializer.ToYaml(TableCreateFixtures.Table());

        Expect.Contains("primaryNameAttribute: tn_name", yaml, "REQ-3: a table with a primary name column should serialise it under the 'primaryNameAttribute' key.");
    }

    // REQ-3: "that column must be a String listed under `attributes`" - an accepted file creates without complaint.
    // Code path: TableCreateValidator.Validate (full rules are in TableCreateValidatorTests).
    [Fact]
    [Trait("Requirement", "REQ-3")]
    public void FromYaml_ThenValidate_AFileNamingAStringColumnAsPrimaryNameIsAccepted()
    {
        var entity = EntityYamlDeserializer.FromYaml(TableCreateFixtures.TableYaml());

        var problems = TableCreateValidator.Validate(entity);

        Expect.Empty(problems, "REQ-3: 'primaryNameAttribute: tn_name' with tn_name as a String column should be accepted.");
    }

    // REQ-9: "`table export` writes `primaryNameAttribute`" - reader half.
    // Code path: EntityJsonDefinitionReader.Read, from the live JSON's "PrimaryNameAttribute".
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public void Read_LiveJsonWithPrimaryNameAttribute_PopulatesPrimaryNameAttribute()
    {
        var entity = new EntityJsonDefinitionReader().Read(LiveAccountJson);

        Expect.Equal("name", entity.PrimaryNameAttribute, "REQ-9: the reader should copy the live table's PrimaryNameAttribute into EntityDefinition.PrimaryNameAttribute.");
    }

    // REQ-9 example: "Exporting `account` yields `primaryNameAttribute: name`."
    // Code path: TableExportService.ExportTableAsync -> EntityJsonDefinitionReader -> EntityYamlSerializer.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task ExportTableAsync_Account_WritesPrimaryNameAttributeName()
    {
        var client = new FakeDataverseClient { EntityDefinitionJson = LiveAccountJson };
        var service = new TableExportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));

        var yaml = await service.ExportTableAsync(EnvironmentUrl, "token", "account", solutionUniqueName: null, CancellationToken.None);

        Expect.Contains("primaryNameAttribute: name", yaml, "REQ-9: exporting 'account' should write 'primaryNameAttribute: name'.");
    }

    // REQ-9: "so export -> rebuild round-trips". Export a live table, read the YAML back, and the
    // create body built from it must carry the same primary name column.
    // Code path: TableExportService -> EntityYamlDeserializer -> EntityMetadataJsonBuilder.BuildCreateBody.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public async Task ExportedYaml_RebuildsIntoACreateBodyWithTheSamePrimaryNameColumn()
    {
        var client = new FakeDataverseClient { EntityDefinitionJson = LiveAccountJson };
        var service = new TableExportService(client, new EntityJsonDefinitionReader(), new AttributeOptionSetFetcher(client));
        var yaml = await service.ExportTableAsync(EnvironmentUrl, "token", "account", solutionUniqueName: null, CancellationToken.None);

        var rebuilt = EntityYamlDeserializer.FromYaml(yaml);
        var body = EntityMetadataJsonBuilder.BuildCreateBody(rebuilt);

        var primary = Assert.Single(body["Attributes"]!.AsArray());
        Expect.Equal("Name", (string?)primary!["SchemaName"], "REQ-9: the rebuilt create body should contain the exported primary name column ('name', schema name 'Name').");
        Expect.Equal(true, (bool?)primary["IsPrimaryName"], "REQ-9: the rebuilt create body's column should be flagged IsPrimaryName.");
    }

    // REQ-9: `WithAttributes` is how `table import` re-sorts live columns for its diff; it must not drop the new key.
    // Code path: EntityDefinition.WithAttributes.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public void WithAttributes_CarriesThePrimaryNameAttributeOver()
    {
        var copy = TableCreateFixtures.Table(primaryNameAttribute: "tn_name").WithAttributes([TableCreateFixtures.NameColumn()]);

        Expect.Equal("tn_name", copy.PrimaryNameAttribute, "REQ-9: EntityDefinition.WithAttributes should keep PrimaryNameAttribute, as it keeps every other table-level property.");
    }
}

using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Builds the <c>EntityMetadata</c> body <c>POST EntityDefinitions</c> needs
/// to create a table, following Microsoft Learn's "Create table definitions"
/// example property for property:
/// https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api
/// - <c>@odata.type</c> <c>Microsoft.Dynamics.CRM.EntityMetadata</c>,
/// <c>SchemaName</c>, <c>DisplayName</c>, <c>DisplayCollectionName</c>,
/// <c>Description</c>, <c>OwnershipType</c>, <c>IsActivity</c>,
/// <c>HasActivities</c>, <c>HasNotes</c>, and an <c>Attributes</c> array
/// holding exactly one <c>StringAttributeMetadata</c> flagged
/// <c>IsPrimaryName: true</c>.
///
/// Only the primary name column is sent: every other column in the file is
/// left for <c>table import</c>, which already knows how to create each type
/// (and its plan/diff/confirm), rather than this growing a second,
/// untested-on-create path for them. The column itself is built by
/// <see cref="AttributeMetadataJsonBuilder.BuildCreateBody"/> - the same body
/// a String column gets from <c>table import</c> (Text format, MaxLength
/// defaulting to 100 as in Learn's example) - plus <c>IsPrimaryName</c>.
/// Labels use <see cref="DataverseLabelJson"/>, as everywhere else.
/// </summary>
public static class EntityMetadataJsonBuilder
{
    /// <exception cref="TableCreateValidationException">
    /// <paramref name="entity"/> has a problem from <see cref="TableCreateValidator.BodyProblems"/> -
    /// a required value missing, an unsupported ownership or activity table,
    /// or a primary name column the body can't be built from. Never defaults
    /// any of them. The schema-name prefix check is <see cref="TableCreateValidator.Validate"/>'s
    /// alone; see that class's doc comment for why.
    /// </exception>
    public static JsonObject BuildCreateBody(EntityDefinition entity)
    {
        var problems = TableCreateValidator.BodyProblems(entity);
        if (problems.Count > 0)
        {
            throw new TableCreateValidationException(problems);
        }

        var primaryName = AttributeMetadataJsonBuilder.BuildCreateBody(TableCreateValidator.FindPrimaryNameColumn(entity)!);
        primaryName["IsPrimaryName"] = true;

        var body = new JsonObject
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata",
            ["SchemaName"] = entity.SchemaName,
            ["DisplayName"] = DataverseLabelJson.Build(entity.DisplayName!),
            ["DisplayCollectionName"] = DataverseLabelJson.Build(entity.PluralDisplayName!),
            ["OwnershipType"] = entity.OwnershipType,
            // Learn lists these three as required on create, so they're sent
            // explicitly; absent in the YAML means Dataverse's default, false
            // (see DefaultValueConventions / docs/yaml-conventions.md).
            ["IsActivity"] = false,
            ["HasActivities"] = entity.HasActivities ?? false,
            ["HasNotes"] = entity.HasNotes ?? false,
            ["Attributes"] = new JsonArray { primaryName },
        };

        if (entity.Description is not null)
        {
            body["Description"] = DataverseLabelJson.Build(entity.Description);
        }

        return body;
    }
}

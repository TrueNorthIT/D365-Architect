using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Pre-flight checks before a table is created from a <c>*.table.yml</c> -
/// nothing here is ever guessed or defaulted. Returns every problem found
/// (empty = creatable), so one run lists everything to fix rather than one
/// problem per attempt. Behaviour pinned by <c>TableCreateValidatorTests</c>.
///
/// Split in two on purpose. <see cref="BodyProblems"/> is everything
/// <see cref="EntityMetadataJsonBuilder.BuildCreateBody"/> would otherwise
/// have to guess at or get structurally wrong (a missing required value,
/// an unsupported table kind, a primary name column the body can't be
/// built from), so the builder refuses on exactly those. The publisher
/// prefix check on <see cref="EntityDefinition.SchemaName"/> is only in
/// <see cref="Validate"/>: it's the same heuristic shape check
/// <see cref="DataverseSchemaNaming.SchemaNamePattern"/> applies to columns
/// (it can't confirm the prefix belongs to a real publisher), and it doesn't
/// change what body gets built - so the builder can still turn an exported
/// system table such as <c>account</c> (schema name "Account") into a body,
/// which is what the export round-trip relies on. <c>table create</c> itself
/// always runs <see cref="Validate"/> first, so it still refuses one.
/// </summary>
public static class TableCreateValidator
{
    /// <summary>
    /// The two <c>OwnershipType</c> values <c>table create</c> supports. The
    /// other values Dataverse defines (<c>TeamOwned</c>, <c>BusinessOwned</c>,
    /// <c>BusinessParented</c>, <c>None</c> - see
    /// https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/ownershiptypes)
    /// are either internal-only or not creatable through a custom table, and
    /// are refused rather than mapped to something else.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedOwnershipTypes = ["UserOwned", "OrganizationOwned"];

    /// <summary>Every problem that stops <paramref name="entity"/> being created; empty when it can be.</summary>
    public static IReadOnlyList<string> Validate(EntityDefinition entity)
    {
        var problems = new List<string>(BodyProblems(entity));

        if (!string.IsNullOrWhiteSpace(entity.SchemaName) && !DataverseSchemaNaming.SchemaNamePattern.IsMatch(entity.SchemaName))
        {
            problems.Add($"schemaName '{entity.SchemaName}' needs a publisher prefix (e.g. 'tn_{entity.SchemaName}') to create {entity.LogicalName}.");
        }

        return problems;
    }

    /// <summary>The subset of <see cref="Validate"/> that <see cref="EntityMetadataJsonBuilder.BuildCreateBody"/> enforces - see this class's own doc comment.</summary>
    internal static IReadOnlyList<string> BodyProblems(EntityDefinition entity)
    {
        var problems = new List<string>();
        var table = entity.LogicalName;

        // A blank value is as good as missing - never a value to default.
        void Require(string? value, string yamlKey)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{yamlKey} is required to create {table}.");
            }
        }

        Require(entity.SchemaName, "schemaName");
        Require(entity.OwnershipType, "ownershipType");
        Require(entity.PrimaryNameAttribute, "primaryNameAttribute");
        Require(entity.DisplayName, "displayName");
        Require(entity.PluralDisplayName, "pluralDisplayName");

        if (!string.IsNullOrWhiteSpace(entity.OwnershipType) && !SupportedOwnershipTypes.Contains(entity.OwnershipType))
        {
            problems.Add($"ownershipType '{entity.OwnershipType}' isn't supported by table create; use {string.Join(" or ", SupportedOwnershipTypes)}.");
        }

        if (entity.IsActivity == true)
        {
            problems.Add("Activity tables aren't supported by table create yet.");
        }

        if (!string.IsNullOrWhiteSpace(entity.PrimaryNameAttribute))
        {
            problems.AddRange(PrimaryNameColumnProblems(entity, entity.PrimaryNameAttribute));
        }

        return problems;
    }

    /// <summary>
    /// The primary name column has to be in the file (the create body is
    /// built from it, never invented), a String, in Text format - Microsoft
    /// Learn, "Create table definitions": "The primary name attribute must
    /// use Text format"
    /// (https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api)
    /// - and carry its own schema name, which
    /// <see cref="AttributeMetadataJsonBuilder.BuildCreateBody"/> never guesses.
    /// </summary>
    private static IEnumerable<string> PrimaryNameColumnProblems(EntityDefinition entity, string primaryNameAttribute)
    {
        var column = FindPrimaryNameColumn(entity);
        if (column is null)
        {
            yield return $"primaryNameAttribute '{primaryNameAttribute}' isn't listed under attributes in {entity.LogicalName}.";
            yield break;
        }

        if (column.Type != "String")
        {
            yield return $"primaryNameAttribute '{primaryNameAttribute}' must be a String column, not {column.Type}.";
            yield break;
        }

        if (column.Format is not null && column.Format != "Text")
        {
            yield return $"primary name column '{column.Name}' must use Text format (Dataverse requires it), not '{column.Format}'.";
        }

        if (string.IsNullOrWhiteSpace(column.SchemaName))
        {
            yield return $"primary name column '{column.Name}' has no schemaName - required to create {entity.LogicalName}.";
        }
    }

    internal static AttributeDefinition? FindPrimaryNameColumn(EntityDefinition entity) =>
        entity.Attributes.FirstOrDefault(a => string.Equals(a.Name, entity.PrimaryNameAttribute, StringComparison.Ordinal));
}

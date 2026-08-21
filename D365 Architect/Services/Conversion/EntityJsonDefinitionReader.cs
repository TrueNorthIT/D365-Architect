using System.Text.Json;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reads an <see cref="EntityDefinition"/> out of a live Dataverse Web API
/// <c>EntityDefinitions(LogicalName='...')</c> response body (with
/// <c>Attributes</c> expanded) — see <see cref="Dataverse.IDataverseClient.GetEntityDefinitionJsonAsync"/>.
///
/// Display text, required level, and validity flags come back as "managed
/// property" objects (<c>{ "Value": ..., "CanBeChanged": ... }</c>) rather
/// than plain scalars; the helpers below unwrap those defensively. Field
/// coverage is checked against Dataverse's own create/update APIs
/// (validated live against a real tenant — see
/// <see cref="Dataverse.IDataverseClient.GetEntityDefinitionJsonAsync"/>),
/// not just what happened to be convenient to read. Not yet covered: a
/// choice column's actual option values (<c>OptionSet</c>) — that needs a
/// separate, per-attribute, type-cast request, not a field on the bulk
/// response this reader consumes.
///
/// Note: unlike <see cref="EntityXmlDefinitionReader"/> (which reads a
/// PhysicalName off legacy SQL-backed unpacked solutions), this reader never
/// sets <see cref="AttributeDefinition.PhysicalName"/> — the modern Web API
/// doesn't expose an equivalent concept, since Dataverse manages its own
/// storage rather than mapping columns onto SQL Server directly.
/// </summary>
public sealed class EntityJsonDefinitionReader : IEntityDefinitionReader
{
    public bool CanRead(string content)
    {
        if (!content.TrimStart().StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("LogicalName", out _)
                && doc.RootElement.TryGetProperty("SchemaName", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public EntityDefinition Read(string content) => Read(content, allowedAttributeMetadataIds: null);

    /// <summary>
    /// As <see cref="Read(string)"/>, but keeps only the attributes whose
    /// MetadataId is in <paramref name="allowedAttributeMetadataIds"/> — how
    /// <see cref="TableExportService"/> scopes an export down to just the
    /// columns a given solution actually customizes (see
    /// <see cref="Dataverse.IDataverseClient.TryGetSolutionAttributeMetadataIdsAsync"/>).
    /// Null means no filtering: every attribute in the response is kept.
    /// </summary>
    public EntityDefinition Read(string content, IReadOnlySet<Guid>? allowedAttributeMetadataIds)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Entity metadata is not well-formed JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("LogicalName", out var logicalNameProperty) || logicalNameProperty.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Entity metadata is missing its 'LogicalName' property.");
            }

            var attributeElements = root.TryGetProperty("Attributes", out var attributesProperty) && attributesProperty.ValueKind == JsonValueKind.Array
                ? attributesProperty.EnumerateArray()
                : Enumerable.Empty<JsonElement>();

            if (allowedAttributeMetadataIds is not null)
            {
                attributeElements = attributeElements.Where(a => IsInAllowedSet(a, allowedAttributeMetadataIds));
            }

            var attributes = attributeElements.Select(ParseAttribute).ToList();

            return new EntityDefinition
            {
                LogicalName = logicalNameProperty.GetString()!,
                SchemaName = GetString(root, "SchemaName"),
                DisplayName = GetLabel(root, "DisplayName"),
                PluralDisplayName = GetLabel(root, "DisplayCollectionName"),
                Description = GetLabel(root, "Description"),
                OwnershipType = GetString(root, "OwnershipType"),
                IsActivity = DefaultValueConventions.TrueOrNull(GetBool(root, "IsActivity")),
                HasActivities = DefaultValueConventions.TrueOrNull(GetBool(root, "HasActivities")),
                HasNotes = DefaultValueConventions.TrueOrNull(GetBool(root, "HasNotes")),
                Attributes = attributes,
            };
        }
    }

    private static bool IsInAllowedSet(JsonElement attribute, IReadOnlySet<Guid> allowedAttributeMetadataIds) =>
        attribute.TryGetProperty("MetadataId", out var idProperty)
        && idProperty.ValueKind == JsonValueKind.String
        && Guid.TryParse(idProperty.GetString(), out var id)
        && allowedAttributeMetadataIds.Contains(id);

    private static AttributeDefinition ParseAttribute(JsonElement attribute)
    {
        if (!attribute.TryGetProperty("LogicalName", out var logicalNameProperty) || logicalNameProperty.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("An attribute is missing its 'LogicalName' property.");
        }

        return new AttributeDefinition
        {
            Name = logicalNameProperty.GetString()!,
            SchemaName = GetString(attribute, "SchemaName"),
            Type = GetString(attribute, "AttributeType") ?? "Unknown",
            DisplayName = GetLabel(attribute, "DisplayName"),
            Description = GetLabel(attribute, "Description"),
            RequiredLevel = DefaultValueConventions.RequiredLevelOrNull(GetManagedPropertyString(attribute, "RequiredLevel")),
            MaxLength = GetInt(attribute, "MaxLength"),
            Precision = GetInt(attribute, "Precision"),
            PrecisionSource = GetInt(attribute, "PrecisionSource"),
            MinValue = GetDouble(attribute, "MinValue"),
            MaxValue = GetDouble(attribute, "MaxValue"),
            Format = GetString(attribute, "Format"),
            Targets = GetStringArray(attribute, "Targets"),
            IsCustomField = DefaultValueConventions.TrueOrNull(GetBool(attribute, "IsCustomAttribute")),
            ValidForAdvancedFind = GetManagedPropertyBool(attribute, "IsValidForAdvancedFind"),
        };
    }

    /// <summary>Reads a Dataverse label object's English (or first available) display text.</summary>
    private static string? GetLabel(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var label) || label.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (label.TryGetProperty("UserLocalizedLabel", out var userLabel)
            && userLabel.ValueKind == JsonValueKind.Object
            && userLabel.TryGetProperty("Label", out var text)
            && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString();
        }

        if (label.TryGetProperty("LocalizedLabels", out var localizedLabels) && localizedLabels.ValueKind == JsonValueKind.Array)
        {
            var first = localizedLabels.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("Label", out var firstText) && firstText.ValueKind == JsonValueKind.String)
            {
                return firstText.GetString();
            }
        }

        return null;
    }

    private static string? GetManagedPropertyString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var managed) || managed.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return managed.TryGetProperty("Value", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetManagedPropertyBool(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var managed) || managed.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return managed.TryGetProperty("Value", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static string? GetString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static bool? GetBool(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static double? GetDouble(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static IReadOnlyList<string>? GetStringArray(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : null;
}

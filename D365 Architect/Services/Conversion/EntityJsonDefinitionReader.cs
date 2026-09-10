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
/// not just what happened to be convenient to read.
///
/// A choice column's actual option values (<c>OptionSet</c>) never come back
/// on the bulk response this reader otherwise consumes — Dataverse only
/// returns them from a separate, per-attribute, type-cast request (see
/// <see cref="OptionSetTypes"/>/<see cref="ListOptionBearingAttributes"/> and
/// <see cref="Dataverse.IDataverseClient.GetAttributeOptionSetJsonAsync"/>).
/// This reader stays purely a parser — it never makes that request itself —
/// so the caller (<see cref="TableExportService"/>/<see cref="TableImportService"/>)
/// fetches each option-bearing attribute's JSON first and passes the results
/// in as <c>optionSetJsonByAttribute</c>, the same way solution-scoping
/// already resolves <c>allowedAttributeMetadataIds</c> before calling
/// <see cref="Read(string, IReadOnlySet{Guid}?, IReadOnlyDictionary{string, string}?)"/>.
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

    /// <summary>
    /// Attribute types whose choice values (<c>OptionSet</c>) need a
    /// separate, per-attribute request — see <see cref="ListOptionBearingAttributes"/>.
    /// </summary>
    public static readonly IReadOnlySet<string> OptionSetTypes = new HashSet<string> { "Boolean", "Picklist", "MultiSelectPicklist", "Status", "State" };

    /// <summary>
    /// Scans a bulk <c>EntityDefinitions(...)?$expand=Attributes</c> response
    /// (without fully parsing it into an <see cref="EntityDefinition"/>) for
    /// every attribute whose type is in <see cref="OptionSetTypes"/> — what
    /// the caller needs to know which per-attribute
    /// <see cref="Dataverse.IDataverseClient.GetAttributeOptionSetJsonAsync"/>
    /// requests to make before calling
    /// <see cref="Read(string, IReadOnlySet{Guid}?, IReadOnlyDictionary{string, string}?)"/>.
    /// </summary>
    public static IReadOnlyList<(string LogicalName, string Type)> ListOptionBearingAttributes(string content)
    {
        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("Attributes", out var attributesProperty) || attributesProperty.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<(string, string)>();
        foreach (var attribute in attributesProperty.EnumerateArray())
        {
            var logicalName = GetString(attribute, "LogicalName");
            var type = GetString(attribute, "AttributeType");
            if (logicalName is not null && type is not null && OptionSetTypes.Contains(type))
            {
                results.Add((logicalName, type));
            }
        }

        return results;
    }

    public EntityDefinition Read(string content) => Read(content, allowedAttributeMetadataIds: null, optionSetJsonByAttribute: null);

    /// <summary>
    /// As <see cref="Read(string)"/>, but keeps only the attributes whose
    /// MetadataId is in <paramref name="allowedAttributeMetadataIds"/> — how
    /// <see cref="TableExportService"/> scopes an export down to just the
    /// columns a given solution actually customizes (see
    /// <see cref="Dataverse.IDataverseClient.TryGetSolutionAttributeMetadataIdsAsync"/>).
    /// Null means no filtering: every attribute in the response is kept.
    /// </summary>
    public EntityDefinition Read(string content, IReadOnlySet<Guid>? allowedAttributeMetadataIds) =>
        Read(content, allowedAttributeMetadataIds, optionSetJsonByAttribute: null);

    /// <summary>
    /// As <see cref="Read(string, IReadOnlySet{Guid}?)"/>, but also merges in
    /// each option-bearing attribute's choice values —
    /// <paramref name="optionSetJsonByAttribute"/> maps a logical name (from
    /// <see cref="ListOptionBearingAttributes"/>) to that attribute's own
    /// <see cref="Dataverse.IDataverseClient.GetAttributeOptionSetJsonAsync"/>
    /// response body. Null (or an attribute missing from it) means "not
    /// fetched" — same as every other absent field, never an error.
    /// </summary>
    public EntityDefinition Read(string content, IReadOnlySet<Guid>? allowedAttributeMetadataIds, IReadOnlyDictionary<string, string>? optionSetJsonByAttribute)
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

            var attributes = attributeElements.Select(a => ParseAttribute(a, optionSetJsonByAttribute)).ToList();

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

    private static AttributeDefinition ParseAttribute(JsonElement attribute, IReadOnlyDictionary<string, string>? optionSetJsonByAttribute)
    {
        if (!attribute.TryGetProperty("LogicalName", out var logicalNameProperty) || logicalNameProperty.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("An attribute is missing its 'LogicalName' property.");
        }

        var logicalName = logicalNameProperty.GetString()!;
        var type = GetString(attribute, "AttributeType") ?? "Unknown";

        OptionSetFields optionSetFields = default;
        if (optionSetJsonByAttribute is not null && optionSetJsonByAttribute.TryGetValue(logicalName, out var optionSetJson))
        {
            optionSetFields = ParseOptionSetJson(optionSetJson, type);
        }

        return new AttributeDefinition
        {
            Name = logicalName,
            SchemaName = GetString(attribute, "SchemaName"),
            Type = type,
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
            Options = optionSetFields.Options,
            GlobalOptionSetName = optionSetFields.GlobalOptionSetName,
            DefaultValue = optionSetFields.DefaultValue,
            TrueOptionLabel = optionSetFields.TrueOptionLabel,
            FalseOptionLabel = optionSetFields.FalseOptionLabel,
        };
    }

    private readonly record struct OptionSetFields(
        IReadOnlyList<AttributeOptionDefinition>? Options,
        string? GlobalOptionSetName,
        bool? DefaultValue,
        string? TrueOptionLabel,
        string? FalseOptionLabel);

    /// <summary>
    /// Parses one <see cref="Dataverse.IDataverseClient.GetAttributeOptionSetJsonAsync"/>
    /// response body — a type-cast attribute with <c>OptionSet</c>/
    /// <c>GlobalOptionSet</c> expanded (see that method's own doc comment for
    /// the request shape). Boolean's <c>OptionSet</c> is a fixed
    /// <c>TrueOption</c>/<c>FalseOption</c> pair rather than a list, so it's
    /// handled separately from Picklist/MultiSelectPicklist/Status's
    /// <c>Options</c> array — confirmed against Microsoft's own documented
    /// shapes for each (see `docs/yaml-conventions.md`).
    /// </summary>
    private static OptionSetFields ParseOptionSetJson(string content, string attributeType)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (attributeType == "Boolean")
        {
            if (!root.TryGetProperty("OptionSet", out var booleanOptionSet) || booleanOptionSet.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            var trueLabel = GetOptionLabel(booleanOptionSet, "TrueOption") ?? "True";
            var falseLabel = GetOptionLabel(booleanOptionSet, "FalseOption") ?? "False";

            return new OptionSetFields(
                Options: null,
                GlobalOptionSetName: null,
                // "false" is Dataverse's own default for a new Boolean
                // column's DefaultValue (see the confirmed create example) —
                // only "true" is worth stating, same TrueOrNull convention
                // used for every other "off by default" flag.
                DefaultValue: DefaultValueConventions.TrueOrNull(GetBool(root, "DefaultValue")),
                TrueOptionLabel: DefaultValueConventions.BooleanOptionLabelOrNull(trueLabel, "True"),
                FalseOptionLabel: DefaultValueConventions.BooleanOptionLabelOrNull(falseLabel, "False"));
        }

        // Picklist / MultiSelectPicklist / Status: a local OptionSet has its
        // own Options array; a global one instead has a Name and its own
        // Options array on GlobalOptionSet, with OptionSet itself null —
        // confirmed against Microsoft's own docs (a picklist attribute never
        // has both at once).
        if (root.TryGetProperty("GlobalOptionSet", out var globalOptionSet) && globalOptionSet.ValueKind == JsonValueKind.Object)
        {
            return new OptionSetFields(Options: null, GlobalOptionSetName: GetString(globalOptionSet, "Name"), DefaultValue: null, TrueOptionLabel: null, FalseOptionLabel: null);
        }

        if (root.TryGetProperty("OptionSet", out var localOptionSet) && localOptionSet.ValueKind == JsonValueKind.Object)
        {
            var options = ParseOptions(localOptionSet);
            return new OptionSetFields(Options: options.Count > 0 ? options : null, GlobalOptionSetName: null, DefaultValue: null, TrueOptionLabel: null, FalseOptionLabel: null);
        }

        return default;
    }

    private static List<AttributeOptionDefinition> ParseOptions(JsonElement optionSet)
    {
        var options = new List<AttributeOptionDefinition>();
        if (!optionSet.TryGetProperty("Options", out var optionsProperty) || optionsProperty.ValueKind != JsonValueKind.Array)
        {
            return options;
        }

        foreach (var option in optionsProperty.EnumerateArray())
        {
            if (option.TryGetProperty("Value", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.Number)
            {
                options.Add(new AttributeOptionDefinition
                {
                    Value = valueProperty.GetInt32(),
                    Label = GetLabel(option, "Label") ?? string.Empty,
                    // Only ever meaningful for a Status option — confirmed
                    // against Microsoft's own StatusOptionMetadata reference
                    // ("State: the state that the status is associated
                    // with"); harmless to read for every other option-bearing
                    // type too, just never populated there.
                    State = option.TryGetProperty("State", out var stateProperty) && stateProperty.ValueKind == JsonValueKind.Number
                        ? stateProperty.GetInt32()
                        : null,
                });
            }
        }

        return options;
    }

    /// <summary>Reads a Boolean OptionSet's TrueOption/FalseOption label — same Label shape as everywhere else, one level deeper.</summary>
    private static string? GetOptionLabel(JsonElement optionSet, string propertyName) =>
        optionSet.TryGetProperty(propertyName, out var option) && option.ValueKind == JsonValueKind.Object
            ? GetLabel(option, "Label")
            : null;

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

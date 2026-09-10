using System.Text.Json;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reads <see cref="GlobalChoiceDefinition"/>s out of a live Dataverse Web
/// API response body — either a single choice (<see cref="Read"/>, from
/// <see cref="Dataverse.IDataverseClient.TryGetGlobalOptionSetJsonAsync"/>)
/// or every choice at once (<see cref="ReadMany"/>, from
/// <see cref="Dataverse.IDataverseClient.GetGlobalOptionSetsJsonAsync"/> —
/// the same <c>{ "value": [...] }</c> collection shape every other bulk
/// Dataverse Web API query uses). A self-contained reader (its own small
/// <c>GetLabel</c>, not shared with <see cref="EntityJsonDefinitionReader"/>'s),
/// matching this codebase's existing convention of each reader owning its
/// own JSON-parsing helpers rather than a shared utility every reader
/// depends on.
/// </summary>
internal static class GlobalChoiceJsonReader
{
    public static GlobalChoiceDefinition Read(string content)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Global choice metadata is not well-formed JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            return ParseChoice(doc.RootElement);
        }
    }

    /// <summary>
    /// As <see cref="Read"/>, but for a bulk <c>{ "value": [...] }</c>
    /// response listing every global choice at once — what <c>choice
    /// export</c> reads. When <paramref name="allowedMetadataIds"/> is
    /// given, keeps only the choices whose <c>MetadataId</c> is in it — how
    /// <see cref="GlobalChoiceExportService"/> scopes an export down to just
    /// the choices a given solution actually customizes (see
    /// <see cref="Dataverse.IDataverseClient.TryGetSolutionOptionSetMetadataIdsAsync"/>).
    /// Null means no filtering: every choice in the response is kept.
    /// </summary>
    public static IReadOnlyList<GlobalChoiceDefinition> ReadMany(string content, IReadOnlySet<Guid>? allowedMetadataIds)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Global choice metadata is not well-formed JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("value", out var valueProperty) || valueProperty.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Global choice metadata is missing its 'value' array.");
            }

            IEnumerable<JsonElement> choices = valueProperty.EnumerateArray();
            if (allowedMetadataIds is not null)
            {
                choices = choices.Where(c => IsInAllowedSet(c, allowedMetadataIds));
            }

            return choices.Select(ParseChoice).ToList();
        }
    }

    private static bool IsInAllowedSet(JsonElement choice, IReadOnlySet<Guid> allowedMetadataIds) =>
        choice.TryGetProperty("MetadataId", out var idProperty)
        && idProperty.ValueKind == JsonValueKind.String
        && Guid.TryParse(idProperty.GetString(), out var id)
        && allowedMetadataIds.Contains(id);

    private static GlobalChoiceDefinition ParseChoice(JsonElement choice)
    {
        if (!choice.TryGetProperty("Name", out var nameProperty) || nameProperty.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Global choice metadata is missing its 'Name' property.");
        }

        var options = new List<AttributeOptionDefinition>();
        if (choice.TryGetProperty("Options", out var optionsProperty) && optionsProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in optionsProperty.EnumerateArray())
            {
                if (option.TryGetProperty("Value", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.Number)
                {
                    options.Add(new AttributeOptionDefinition { Value = valueProperty.GetInt32(), Label = GetLabel(option, "Label") ?? string.Empty });
                }
            }
        }

        return new GlobalChoiceDefinition
        {
            Name = nameProperty.GetString()!,
            DisplayName = GetLabel(choice, "DisplayName"),
            Description = GetLabel(choice, "Description"),
            Options = options.Count > 0 ? options : null,
        };
    }

    /// <summary>Reads a Dataverse label object's English (or first available) display text — same shape/fallback as every other reader in this tool.</summary>
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
}

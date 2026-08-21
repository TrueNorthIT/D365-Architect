using System.Text.Json;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reads a set of <see cref="FormDefinition"/>s out of a live Dataverse Web
/// API <c>systemforms</c> response body — see
/// <see cref="Dataverse.IDataverseClient.GetFormDefinitionsJsonAsync"/>. The
/// <see cref="ViewJsonDefinitionReader"/> counterpart for forms.
/// </summary>
public sealed class FormJsonDefinitionReader
{
    /// <summary>Maps a systemform's raw `type` to its option set's own label.</summary>
    private static readonly IReadOnlyDictionary<int, string> TypeNames = new Dictionary<int, string>
    {
        [0] = "Dashboard",
        [1] = "AppointmentBook",
        [2] = "Main",
        [3] = "MiniCampaignBO",
        [4] = "Preview",
        [5] = "Mobile - Express",
        [6] = "Quick View Form",
        [7] = "Quick Create",
        [8] = "Dialog",
        [9] = "Task Flow Form",
        [10] = "InteractionCentricDashboard",
        [11] = "Card",
        [12] = "Main - Interactive experience",
        [13] = "Contextual Dashboard",
        [100] = "Other",
        [101] = "MainBackup",
        [102] = "AppointmentBookBackup",
        [103] = "Power BI Dashboard",
    };

    public IReadOnlyList<FormDefinition> Read(string content) => Read(content, allowedFormIds: null);

    /// <summary>
    /// As <see cref="Read(string)"/>, but keeps only the forms whose formid
    /// is in <paramref name="allowedFormIds"/> — how <see cref="FormExportService"/>
    /// scopes an export down to just the forms a given solution actually
    /// customizes (see <see cref="Dataverse.IDataverseClient.TryGetSolutionSystemFormIdsAsync"/>).
    /// Null means no filtering: every form in the response is kept.
    /// </summary>
    public IReadOnlyList<FormDefinition> Read(string content, IReadOnlySet<Guid>? allowedFormIds)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Form metadata is not well-formed JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            var formElements = doc.RootElement.TryGetProperty("value", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.Array
                ? valueProperty.EnumerateArray()
                : Enumerable.Empty<JsonElement>();

            if (allowedFormIds is not null)
            {
                formElements = formElements.Where(f => IsInAllowedSet(f, allowedFormIds));
            }

            return formElements.Select(ParseForm).ToList();
        }
    }

    private static bool IsInAllowedSet(JsonElement form, IReadOnlySet<Guid> allowedFormIds) =>
        form.TryGetProperty("formid", out var idProperty)
        && idProperty.ValueKind == JsonValueKind.String
        && Guid.TryParse(idProperty.GetString(), out var id)
        && allowedFormIds.Contains(id);

    private static FormDefinition ParseForm(JsonElement form)
    {
        if (!form.TryGetProperty("name", out var nameProperty) || nameProperty.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A form is missing its 'name' property.");
        }

        var name = nameProperty.GetString()!;

        return new FormDefinition
        {
            Name = name,
            Entity = GetString(form, "objecttypecode") ?? throw new InvalidDataException($"Form '{name}' is missing its 'objecttypecode' property."),
            Description = GetString(form, "description"),
            Type = TypeOrNull(GetInt(form, "type")),
            IsDefault = DefaultValueConventions.TrueOrNull(GetBool(form, "isdefault")),
            FormActivationState = FormActivationStateOrNull(GetInt(form, "formactivationstate")),
            IsCustomizable = GetManagedPropertyBool(form, "iscustomizable"),
            FormXml = GetString(form, "formxml"),
        };
    }

    /// <summary>
    /// 2 (Main) is Dataverse's ordinary form and by far the common case, so
    /// it's treated the same way as any other platform-default value
    /// elsewhere (see <see cref="DefaultValueConventions"/>) and left out.
    /// </summary>
    private static string? TypeOrNull(int? type) =>
        type is null or 2 ? null : TypeNames.GetValueOrDefault(type.Value, type.Value.ToString());

    /// <summary>1 (Active) is the common case for any form actually in use; only the exceptional "Inactive" (unpublished draft) is worth stating.</summary>
    private static string? FormActivationStateOrNull(int? formActivationState) =>
        formActivationState == 0 ? "Inactive" : null;

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
}

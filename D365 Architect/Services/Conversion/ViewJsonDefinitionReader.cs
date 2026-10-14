using System.Text.Json;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reads a set of <see cref="ViewDefinition"/>s out of a live Dataverse Web
/// API <c>savedqueries</c> response body — see
/// <see cref="Dataverse.IDataverseClient.GetViewDefinitionsJsonAsync"/>.
///
/// Not yet covered: decomposing <c>layoutxml</c>'s column list into a
/// friendlier shape (widths, sort order, ...). Unlike an entity's Attributes,
/// there's no bulk metadata endpoint to fall back on here either way — the
/// XML itself is the only source — so it's kept verbatim for now rather than
/// guessed at ahead of a real need.
/// </summary>
public sealed class ViewJsonDefinitionReader
{
    /// <summary>Maps a savedquery's raw `querytype` to the SDK's own named constant for it.</summary>
    private static readonly IReadOnlyDictionary<int, string> QueryTypeNames = new Dictionary<int, string>
    {
        [0] = "MainApplicationView",
        [1] = "AdvancedSearch",
        [2] = "SubGrid",
        [4] = "QuickFindSearch",
        [8] = "Reporting",
        [16] = "OfflineFilters",
        [64] = "LookupView",
        [128] = "SMAppointmentBookView",
        [256] = "OutlookFilters",
        [512] = "AddressBookFilters",
        [1024] = "MainApplicationViewWithoutSubject",
        [2048] = "SavedQueryTypeOther",
        [4096] = "InteractiveWorkflowView",
        [8192] = "OfflineTemplate",
        [16384] = "CustomDefinedView",
        [65536] = "ExportFieldTranslationsView",
        [131072] = "OutlookTemplate",
    };

    public IReadOnlyList<ViewDefinition> Read(string content) => Read(content, allowedSavedQueryIds: null);

    /// <summary>
    /// As <see cref="Read(string)"/>, but keeps only the views whose
    /// savedqueryid is in <paramref name="allowedSavedQueryIds"/> — how
    /// <see cref="ViewExportService"/> scopes an export down to just the
    /// views a given solution actually customizes (see
    /// <see cref="Dataverse.IDataverseClient.TryGetSolutionSavedQueryIdsAsync"/>).
    /// Null means no filtering: every view in the response is kept.
    /// </summary>
    public IReadOnlyList<ViewDefinition> Read(string content, IReadOnlySet<Guid>? allowedSavedQueryIds)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"View metadata is not well-formed JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            var viewElements = doc.RootElement.TryGetProperty("value", out var valueProperty) && valueProperty.ValueKind == JsonValueKind.Array
                ? valueProperty.EnumerateArray()
                : Enumerable.Empty<JsonElement>();

            if (allowedSavedQueryIds is not null)
            {
                viewElements = viewElements.Where(v => IsInAllowedSet(v, allowedSavedQueryIds));
            }

            return viewElements.Select(ParseView).ToList();
        }
    }

    private static bool IsInAllowedSet(JsonElement view, IReadOnlySet<Guid> allowedSavedQueryIds) =>
        view.TryGetProperty("savedqueryid", out var idProperty)
        && idProperty.ValueKind == JsonValueKind.String
        && Guid.TryParse(idProperty.GetString(), out var id)
        && allowedSavedQueryIds.Contains(id);

    private static ViewDefinition ParseView(JsonElement view)
    {
        if (!view.TryGetProperty("name", out var nameProperty) || nameProperty.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("A view is missing its 'name' property.");
        }

        var name = nameProperty.GetString()!;

        return new ViewDefinition
        {
            Name = name,
            Entity = GetString(view, "returnedtypecode") ?? throw new InvalidDataException($"View '{name}' is missing its 'returnedtypecode' property."),
            Description = GetString(view, "description"),
            QueryType = QueryTypeOrNull(GetInt(view, "querytype")),
            IsDefault = DefaultValueConventions.TrueOrNull(GetBool(view, "isdefault")),
            IsQuickFindQuery = DefaultValueConventions.TrueOrNull(GetBool(view, "isquickfindquery")),
            IsUserDefined = DefaultValueConventions.TrueOrNull(GetBool(view, "isuserdefined")),
            IsCustomizable = DefaultValueConventions.FalseOrNull(GetManagedPropertyBool(view, "iscustomizable")),
            // Null for a handful of internal system views (e.g. "{Entity}
            // BulkOperation View") that don't carry one — confirmed live,
            // not assumed; see ViewDefinition.FetchXml.
            FetchXml = GetString(view, "fetchxml"),
            LayoutXml = GetString(view, "layoutxml"),
        };
    }

    /// <summary>
    /// 0 (MainApplicationView) is Dataverse's ordinary system view and by far
    /// the common case, so it's treated the same way as any other
    /// platform-default value elsewhere (see <see cref="DefaultValueConventions"/>)
    /// and left out. Anything else maps to its SDK constant name, falling
    /// back to the raw number for a querytype this list doesn't yet name
    /// (e.g. CopilotView, whose numeric value Microsoft's docs don't
    /// currently publish).
    /// </summary>
    private static string? QueryTypeOrNull(int? queryType) =>
        queryType is null or 0 ? null : QueryTypeNames.GetValueOrDefault(queryType.Value, queryType.Value.ToString());

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

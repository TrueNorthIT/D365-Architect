using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
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

    public IReadOnlyList<FormSummary> ReadSummaries(string content) => ReadSummaries(content, allowedFormIds: null);

    /// <summary>
    /// As <see cref="Read(string, IReadOnlySet{Guid}?)"/>, but reads only
    /// enough of each form (id, name, type) to list it for a human to
    /// choose from — never touches <c>formxml</c> at all, so this is cheap
    /// even for a table whose forms are individually large. See
    /// <see cref="Dataverse.IDataverseClient.GetFormSummariesJsonAsync"/>.
    /// </summary>
    public IReadOnlyList<FormSummary> ReadSummaries(string content, IReadOnlySet<Guid>? allowedFormIds)
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

            return formElements.Select(ParseSummary).ToList();
        }
    }

    private static FormSummary ParseSummary(JsonElement form)
    {
        var formId = form.TryGetProperty("formid", out var idProperty) && idProperty.ValueKind == JsonValueKind.String && Guid.TryParse(idProperty.GetString(), out var id)
            ? id
            : Guid.Empty;
        var name = GetString(form, "name") ?? "(unnamed form)";
        var type = GetInt(form, "type") is { } typeValue ? TypeNames.GetValueOrDefault(typeValue, typeValue.ToString()) : "Unknown";

        return new FormSummary(formId, name, type);
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
        var formXmlRoot = TryParseFormXml(GetString(form, "formxml"));
        var additionalControls = ParseControlDescriptions(formXmlRoot);

        return new FormDefinition
        {
            Name = name,
            Entity = GetString(form, "objecttypecode") ?? throw new InvalidDataException($"Form '{name}' is missing its 'objecttypecode' property."),
            Description = GetString(form, "description"),
            Type = TypeOrNull(GetInt(form, "type")),
            IsDefault = DefaultValueConventions.TrueOrNull(GetBool(form, "isdefault")),
            FormActivationState = FormActivationStateOrNull(GetInt(form, "formactivationstate")),
            IsCustomizable = DefaultValueConventions.FalseOrNull(GetManagedPropertyBool(form, "iscustomizable")),
            Tabs = formXmlRoot?.Element("tabs")?.Elements("tab").Select(tab => ParseTab(tab, additionalControls)).ToList() ?? [],
            HeaderControls = NullIfEmpty(ParseControls(formXmlRoot?.Element("header"), additionalControls)),
            FooterControls = NullIfEmpty(ParseControls(formXmlRoot?.Element("footer"), additionalControls)),
            Ancestor = (string?)formXmlRoot?.Element("ancestor")?.Attribute("id"),
            HiddenFields = NullIfEmpty(formXmlRoot?.Element("hiddencontrols")?.Elements("data").Select(ParseHiddenField).ToList()),
            DisplayCondition = formXmlRoot?.Element("DisplayConditions") is { } displayConditions ? ParseDisplayCondition(displayConditions) : null,
            Libraries = NullIfEmpty(formXmlRoot?.Element("formLibraries")?.Elements("Library").Select(ParseLibrary).ToList()),
            Events = NullIfEmpty(formXmlRoot?.Element("events")?.Elements("event").Select(ParseEvent).ToList()),
        };
    }

    private static FormHiddenField ParseHiddenField(XElement data) => new()
    {
        Field = (string?)data.Attribute("datafieldname") ?? (string?)data.Attribute("id") ?? "",
        ClassId = (string?)data.Attribute("classid"),
    };

    private static FormDisplayCondition ParseDisplayCondition(XElement displayConditions) => new()
    {
        FallbackForm = DefaultValueConventions.TrueOrNull((string?)displayConditions.Attribute("FallbackForm") == "true"),
        Order = (int?)displayConditions.Attribute("Order"),
        Roles = NullIfEmpty(displayConditions.Elements("Role").Select(role => (string?)role.Attribute("Id")).Where(id => id is not null).Select(id => id!).ToList()),
    };

    private static FormLibrary ParseLibrary(XElement library) => new()
    {
        Name = (string?)library.Attribute("name") ?? "",
    };

    private static FormEvent ParseEvent(XElement eventElement) => new()
    {
        Name = (string?)eventElement.Attribute("name"),
        Attribute = (string?)eventElement.Attribute("attribute"),
        Active = (bool?)eventElement.Attribute("active"),
        Handlers = NullIfEmpty(eventElement.Element("Handlers")?.Elements("Handler").Select(ParseEventHandler).ToList()),
        InternalHandlers = NullIfEmpty(eventElement.Element("InternalHandlers")?.Elements("Handler").Select(ParseEventHandler).ToList()),
    };

    private static FormEventHandler ParseEventHandler(XElement handler) => new()
    {
        FunctionName = (string?)handler.Attribute("functionName") ?? "",
        LibraryName = (string?)handler.Attribute("libraryName") ?? "",
        Enabled = (bool?)handler.Attribute("enabled"),
        PassExecutionContext = (bool?)handler.Attribute("passExecutionContext"),
    };

    /// <summary>
    /// Reads FormXML's separate `&lt;controlDescriptions&gt;` section into a
    /// lookup by `forControl` — confirmed against a real tenant to match a
    /// control's own `uniqueid` attribute, not the cell's or the control's
    /// own `id`. See <see cref="FormAdditionalControl"/>.
    ///
    /// A `forControl` that matches no control actually on the form (seen
    /// live — a leftover from a control that was since removed without its
    /// `controlDescription` being cleaned up) is silently dropped rather
    /// than surfaced some other way: this lookup is only ever consulted by
    /// a real control's own `uniqueid` while parsing that control, so an
    /// entry nothing ever looks up for just never gets used. Accepted as a
    /// documented gap rather than modeled (e.g. as some
    /// "OrphanedAdditionalControls" list) — it describes a control that's
    /// already gone, not one currently on the form.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<FormAdditionalControl>> ParseControlDescriptions(XElement? formRoot)
    {
        var result = new Dictionary<string, IReadOnlyList<FormAdditionalControl>>();

        var descriptions = formRoot?.Element("controlDescriptions")?.Elements("controlDescription") ?? Enumerable.Empty<XElement>();
        foreach (var description in descriptions)
        {
            var forControl = (string?)description.Attribute("forControl");
            if (string.IsNullOrEmpty(forControl))
            {
                continue;
            }

            var additional = description.Elements("customControl").Select(ParseAdditionalControl).ToList();
            if (additional.Count > 0)
            {
                result[forControl] = additional;
            }
        }

        return result;
    }

    private static FormAdditionalControl ParseAdditionalControl(XElement customControl) => new()
    {
        Id = (string?)customControl.Attribute("id"),
        Name = (string?)customControl.Attribute("name"),
        FormFactor = (int?)customControl.Attribute("formFactor"),
        Version = (string?)customControl.Attribute("version"),
        Parameters = customControl.Element("parameters") is { } parameters ? ConvertToObject(parameters) : null,
    };

    /// <summary>
    /// Parses FormXML defensively rather than letting one form's broken or
    /// unusual markup fail the whole (many-forms-per-table) export: null
    /// FormXML and unparseable FormXML both just mean "nothing to decompose"
    /// rather than an error.
    /// </summary>
    private static XElement? TryParseFormXml(string? formXml)
    {
        if (string.IsNullOrEmpty(formXml))
        {
            return null;
        }

        try
        {
            return XElement.Parse(formXml);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>As <see cref="TryParseFormXml"/>, for the embedded-XML values <see cref="ConvertToObject"/> can hit inside a control's parameters.</summary>
    private static XElement? TryParseEmbeddedXml(string xml)
    {
        try
        {
            return XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static FormTab ParseTab(XElement tab, IReadOnlyDictionary<string, IReadOnlyList<FormAdditionalControl>> additionalControls) => new()
    {
        Name = (string?)tab.Attribute("name"),
        Label = FirstLabelText(tab.Element("labels")),
        Columns = tab.Element("columns")?.Elements("column").Select(column => ParseColumn(column, additionalControls)).ToList() ?? [],
    };

    private static FormColumn ParseColumn(XElement column, IReadOnlyDictionary<string, IReadOnlyList<FormAdditionalControl>> additionalControls) => new()
    {
        Width = (string?)column.Attribute("width"),
        Sections = column.Element("sections")?.Elements("section").Select(section => ParseSection(section, additionalControls)).ToList() ?? [],
    };

    private static FormSection ParseSection(XElement section, IReadOnlyDictionary<string, IReadOnlyList<FormAdditionalControl>> additionalControls) => new()
    {
        Name = (string?)section.Attribute("name"),
        Label = FirstLabelText(section.Element("labels")),
        // The section's own "columns" attribute is a string of one digit
        // per sub-column (e.g. "11" = 2 equal columns) rather than a number
        // to parse — its length, not its numeric value, is the column count.
        Columns = (string?)section.Attribute("columns") is { Length: > 1 } columns ? columns.Length : null,
        Controls = ParseControls(section, additionalControls),
    };

    /// <summary>
    /// Reads the `&lt;cell&gt;`/`&lt;control&gt;` pairs out of a container's
    /// `&lt;rows&gt;` — shared by sections, headers, and footers, which all
    /// use the same rows/row/cell/control shape. Only cells with no control
    /// at all (pure layout spacers, carrying nothing worth keeping) are
    /// skipped; every real control — bound to a field or not — is kept, see
    /// <see cref="FormControl"/>'s own doc comment for why.
    /// </summary>
    private static IReadOnlyList<FormControl> ParseControls(XElement? container, IReadOnlyDictionary<string, IReadOnlyList<FormAdditionalControl>> additionalControls)
    {
        var rows = container?.Element("rows")?.Elements("row") ?? Enumerable.Empty<XElement>();

        return rows
            .SelectMany(row => row.Elements("cell"))
            .Select(cell => ParseControl(cell, additionalControls))
            .Where(control => control is not null)
            .Select(control => control!)
            .ToList();
    }

    private static FormControl? ParseControl(XElement cell, IReadOnlyDictionary<string, IReadOnlyList<FormAdditionalControl>> additionalControls)
    {
        var control = cell.Element("control");
        var id = (string?)control?.Attribute("id");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var uniqueId = (string?)control!.Attribute("uniqueid");

        return new FormControl
        {
            Id = id,
            Field = (string?)control.Attribute("datafieldname"),
            Label = FirstLabelText(cell.Element("labels")),
            ClassId = (string?)control.Attribute("classid"),
            Disabled = DefaultValueConventions.TrueOrNull((string?)control.Attribute("disabled") == "true"),
            Visible = DefaultValueConventions.FalseOrNull((bool?)cell.Attribute("visible")),
            ColumnSpan = (int?)cell.Attribute("colspan") is { } colspan and > 1 ? colspan : null,
            RowSpan = (int?)cell.Attribute("rowspan") is { } rowspan and > 1 ? rowspan : null,
            Parameters = control.Element("parameters") is { } parameters ? ConvertToObject(parameters) : null,
            AdditionalControls = uniqueId is not null && additionalControls.TryGetValue(uniqueId, out var additional) ? additional : null,
            // Not documented as a valid child of <cell> in Microsoft's own
            // published FormXML schema (only <labels>/<control> are) — real
            // forms have it anyway, confirmed live rather than assumed.
            Events = NullIfEmpty(cell.Element("events")?.Elements("event").Select(ParseEvent).ToList()),
        };
    }

    /// <summary>
    /// Converts a `&lt;parameters&gt;` element into plain YAML-serialisable
    /// data structurally — not byte-for-byte the same shape as the source
    /// XML, since that's not actually the goal: an eventual "apply" only
    /// needs to be able to derive the original from this YAML, not have
    /// this YAML mirror the XML's own conventions. So rather than the
    /// terser but cryptic `@name`/`#text` XML-to-JSON convention, this uses
    /// plain words instead:
    ///
    /// - A leaf element (no attributes, no children) becomes its text
    ///   value — recursing when that text is itself a serialised XML
    ///   fragment (e.g. a quick view control's QuickForms; some Dataverse
    ///   parameters are double-encoded this way). Embedded JSON (e.g. a
    ///   timeline control's *ConfigurationJSON parameters) is left as a
    ///   plain string — that's a different, more common Dataverse pattern
    ///   this tool doesn't also parse.
    /// - An element with attributes and/or children becomes a map: its
    ///   attributes (if any) grouped under `attributes`, its own text (if
    ///   any, alongside attributes) under `value`, and each child element
    ///   under its own name — a list when a name repeats. Element/attribute
    ///   names themselves are kept as Dataverse names them (e.g.
    ///   `RelationshipName`, `entityname`) rather than re-cased: those are
    ///   already self-describing, and guessing at word boundaries to
    ///   reformat them risks getting it wrong for exactly the same reason
    ///   guessing at <see cref="FormControl.ClassId"/>'s meaning would.
    ///   (`attributes`/`value` could theoretically collide with a real
    ///   child element of the same name — not seen in practice, but a
    ///   known, accepted trade-off of choosing readability over an
    ///   unambiguous-but-cryptic marker like `@`/`#`.)
    /// - A literal "false" (case-insensitive) is dropped. Unlike these
    ///   parameters' other values (strings, GUIDs, numbers — no reliable
    ///   source says what any of those default to), a boolean has only two
    ///   possible states: every one of these parameters is declared
    ///   `type="xs:boolean" minOccurs="0"` with no XSD default, and every
    ///   sample gathered from a real tenant agrees "false" is either the
    ///   only value seen or the overwhelming majority — so omitting the
    ///   element (this tool's own choice, not the schema's) and writing it
    ///   as false come to the same thing either way. "true" is always kept,
    ///   so the meaningful, deliberately-set state is never hidden — and
    ///   still overridable by simply writing it back into the YAML.
    ///   Dropping it turns an element/attribute/list entry empty in turn,
    ///   all the way up to <see cref="FormControl.Parameters"/> itself
    ///   coming back null when a control's whole parameter block was just
    ///   defaults.
    /// See <see cref="FormControl.Parameters"/>.
    /// </summary>
    private static object? ConvertToObject(XElement element)
    {
        var children = element.Elements().ToList();
        var attributes = element.Attributes().ToList();

        if (children.Count == 0 && attributes.Count == 0)
        {
            var value = element.Value;
            if (string.IsNullOrEmpty(value))
            {
                // A genuinely empty leaf, e.g. a self-closing <parameters />
                // (confirmed live: a base control's own default customControl
                // entry in controlDescriptions has nothing else to say) — not
                // the same as omitting the element entirely, but there's
                // nothing here worth keeping either.
                return null;
            }

            if (value.TrimStart().StartsWith('<') && TryParseEmbeddedXml(value) is { } embedded)
            {
                return ConvertToObject(embedded);
            }

            return IsFalse(value) ? null : value;
        }

        var map = new Dictionary<string, object>();

        var keptAttributes = attributes.Where(a => !IsFalse(a.Value)).ToDictionary(a => a.Name.LocalName, object (a) => a.Value);
        if (keptAttributes.Count > 0)
        {
            map["attributes"] = keptAttributes;
        }

        if (children.Count == 0)
        {
            if (!string.IsNullOrEmpty(element.Value) && !IsFalse(element.Value))
            {
                map["value"] = element.Value;
            }

            return map.Count > 0 ? map : null;
        }

        foreach (var group in children.GroupBy(child => child.Name.LocalName))
        {
            var values = group.Select(ConvertToObject).Where(value => value is not null).Select(value => value!).ToList();
            if (values.Count == 0)
            {
                continue;
            }

            map[group.Key] = values.Count == 1 ? values[0] : values;
        }

        return map.Count > 0 ? map : null;
    }

    private static bool IsFalse(string value) => string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Picks the English (1033) entry from a FormXML `&lt;labels&gt;` container, falling back to whichever entry comes first if English isn't present. Blank labels (common on unlabeled/spacer cells) are treated as no label.</summary>
    private static string? FirstLabelText(XElement? labels)
    {
        if (labels is null)
        {
            return null;
        }

        var entries = labels.Elements("label").ToList();
        var english = entries.FirstOrDefault(e => (string?)e.Attribute("languagecode") == "1033");
        var text = (string?)(english ?? entries.FirstOrDefault())?.Attribute("description");
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T>? items) => items is { Count: > 0 } ? items : null;

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

using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Builds FormXML back out of a curated <see cref="FormDefinition"/> — the
/// reverse of <see cref="FormJsonDefinitionReader"/>, applying every
/// reconstruction rule documented in `docs/yaml-conventions.md` (an absent
/// value means Dataverse's own default; an omitted boolean inside
/// <see cref="FormControl.Parameters"/> is never re-added, since omitting
/// it was already equivalent to writing `false`; a converted structure is
/// walked back into elements/attributes the same way it came from them).
///
/// <see cref="Write"/> takes an optional <c>existingForm</c> — the form's
/// current, live FormXML (fetched by <c>form build-xml</c> via
/// <see cref="Dataverse.IDataverseClient.TryGetSystemFormXmlAsync"/> before
/// calling this). When given, only the top-level elements this tool
/// actually manages (<c>ancestor</c>, <c>hiddencontrols</c>, <c>tabs</c>,
/// <c>header</c>/<c>footer</c>, <c>events</c>, <c>formLibraries</c>,
/// <c>DisplayConditions</c>, <c>controlDescriptions</c>) are replaced on
/// that document — everything else on it (<c>Navigation</c>,
/// <c>clientresources</c>, <c>RibbonDiffXml</c>, <c>formparameters</c>,
/// <c>externaldependencies</c>, a tab's own <c>tabheader</c>/<c>tabfooter</c>,
/// and the form root's own chrome attributes like <c>showImage</c>/
/// <c>headerdensity</c>) is left exactly as it was, because it's still
/// right there in the document being patched rather than something this
/// tool has to reconstruct from a YAML representation that never captured
/// it. That's the difference between the two ways this can be called:
///
/// - With <c>existingForm</c>: safe to treat as a faithful update to an
///   existing, richly customized form — nothing this tool doesn't model
///   gets lost, because nothing this tool doesn't model is ever touched.
/// - Without it (a brand-new form this YAML describes but that hasn't been
///   created in Dataverse yet — <c>TryGetSystemFormXmlAsync</c> returns null
///   for that case): built from scratch, so anything in that "documented
///   gap" list in <see cref="Models.FormDefinition"/>'s own doc comment
///   genuinely won't be there, for the obvious reason that there's no prior
///   document to have carried it.
///
/// Dashboards are refused outright either way, not just in the from-scratch
/// case: a dashboard's tiles (`&lt;Visualization&gt;`/`&lt;SavedQuery&gt;`)
/// live inside `&lt;tabs&gt;`, which this tool always replaces wholesale, so
/// patching mode can't protect them either.
/// </summary>
public static class FormXmlWriter
{
    /// <param name="form">The curated form to render as FormXML.</param>
    /// <param name="existingForm">
    /// The form's current live FormXML root, if it already exists in
    /// Dataverse — see this class's own doc comment for what passing (or
    /// omitting) this changes. A defensive deep copy is taken; the caller's
    /// tree is never mutated.
    /// </param>
    /// <exception cref="NotSupportedException"><paramref name="form"/> is a dashboard — this tool never decomposes dashboard tiles, so reconstructing one would silently produce a dashboard with none of its original visualizations.</exception>
    public static string Write(FormDefinition form, XElement? existingForm = null)
    {
        if (form.Type is "Dashboard" or "InteractionCentricDashboard" or "Contextual Dashboard" or "Power BI Dashboard")
        {
            throw new NotSupportedException(
                $"'{form.Name}' is a {form.Type} form. This tool never decomposes dashboard tiles (see FormDefinition's own doc comment), " +
                "so rebuilding FormXML for one would produce a dashboard with none of its original visualizations rather than a faithful copy.");
        }

        // Collects controlDescriptions entries as controls that need one are
        // written — see WriteCell. Order doesn't matter: FormType declares
        // its top-level children as an unordered xs:all group.
        var controlDescriptions = new List<XElement>();

        var formEl = existingForm is not null ? new XElement(existingForm) : new XElement("form");

        ReplaceManagedChild(formEl, "ancestor", form.Ancestor is not null
            ? new XElement("ancestor", new XAttribute("id", form.Ancestor))
            : null);

        ReplaceManagedChild(formEl, "hiddencontrols", form.HiddenFields is { Count: > 0 }
            ? new XElement("hiddencontrols", form.HiddenFields.Select(WriteHiddenField))
            : null);

        ReplaceManagedChild(formEl, "tabs", new XElement("tabs", form.Tabs.Select(tab => WriteTab(tab, controlDescriptions))));

        ReplaceManagedChild(formEl, "header", form.HeaderControls is { Count: > 0 }
            ? WriteHeaderOrFooter("header", form.HeaderControls, controlDescriptions)
            : null);

        ReplaceManagedChild(formEl, "footer", form.FooterControls is { Count: > 0 }
            ? WriteHeaderOrFooter("footer", form.FooterControls, controlDescriptions)
            : null);

        ReplaceManagedChild(formEl, "events", form.Events is { Count: > 0 }
            ? new XElement("events", form.Events.Select(WriteEvent))
            : null);

        ReplaceManagedChild(formEl, "formLibraries", form.Libraries is { Count: > 0 }
            ? new XElement("formLibraries", form.Libraries.Select(WriteLibrary))
            : null);

        ReplaceManagedChild(formEl, "DisplayConditions", form.DisplayCondition is not null
            ? WriteDisplayCondition(form.DisplayCondition)
            : null);

        // Populated as a side effect of writing tabs/header/footer above, so
        // this has to come last.
        ReplaceManagedChild(formEl, "controlDescriptions", controlDescriptions.Count > 0
            ? new XElement("controlDescriptions", controlDescriptions)
            : null);

        return formEl.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Replaces one top-level `&lt;form&gt;` child element this tool
    /// manages — in place, keeping its original position, when
    /// <paramref name="formEl"/> already had one (so patching an existing
    /// document produces a smaller diff than removing and re-appending
    /// would); appended when there wasn't one yet; removed entirely when
    /// <paramref name="replacement"/> is null. Every element name this is
    /// never called with (`Navigation`, `clientresources`, `RibbonDiffXml`,
    /// ...) is, by construction, never looked at — that's what lets it
    /// survive a patch untouched even though this tool has never modeled it.
    /// </summary>
    private static void ReplaceManagedChild(XElement formEl, string elementName, XElement? replacement)
    {
        var existing = formEl.Element(elementName);

        if (replacement is null)
        {
            existing?.Remove();
            return;
        }

        if (existing is not null)
        {
            existing.ReplaceWith(replacement);
        }
        else
        {
            formEl.Add(replacement);
        }
    }

    private static XElement WriteHiddenField(FormHiddenField field)
    {
        var element = new XElement("data", new XAttribute("id", field.Field), new XAttribute("datafieldname", field.Field));
        if (field.ClassId is not null)
        {
            element.SetAttributeValue("classid", field.ClassId);
        }

        return element;
    }

    private static XElement WriteTab(FormTab tab, List<XElement> controlDescriptions)
    {
        // A real tab (e.g. a "Card" form's tabs) can genuinely have no
        // `name` attribute at all, only a label — this key is only ever
        // used to seed deterministic ids further down, never written out
        // as a `name` attribute unless the tab actually had one.
        var scopeKey = tab.Name ?? tab.Label ?? "tab";
        var element = new XElement("tab", new XAttribute("id", DeterministicGuid("tab", scopeKey)));

        if (tab.Name is not null)
        {
            element.SetAttributeValue("name", tab.Name);
        }

        if (tab.Label is not null)
        {
            element.Add(WriteLabels(tab.Label));
        }

        element.Add(new XElement("columns", tab.Columns.Select(column => WriteColumn(scopeKey, column, controlDescriptions))));
        return element;
    }

    private static XElement WriteColumn(string tabScopeKey, FormColumn column, List<XElement> controlDescriptions) => new(
        "column",
        new XAttribute("width", column.Width ?? "100%"),
        new XElement("sections", column.Sections.Select(section => WriteSection(tabScopeKey, section, controlDescriptions))));

    private static XElement WriteSection(string tabScopeKey, FormSection section, List<XElement> controlDescriptions)
    {
        // Same reasoning as WriteTab's scopeKey: only ever written out as
        // `name` when the section genuinely had one.
        var sectionScopeKey = section.Name ?? section.Label ?? "section";
        var columns = section.Columns ?? 1;
        var scopeKey = $"{tabScopeKey}/{sectionScopeKey}";

        var element = new XElement("section", new XAttribute("id", DeterministicGuid("section", scopeKey)));

        if (section.Name is not null)
        {
            element.SetAttributeValue("name", section.Name);
        }

        if (columns > 1)
        {
            element.SetAttributeValue("columns", new string('1', columns));
        }

        if (section.Label is not null)
        {
            element.Add(WriteLabels(section.Label));
        }

        // Controls are a flat, row-major list (see FormSection.Columns'
        // doc comment) — regrouping into rows of `columns` cells each is
        // exactly how they were flattened in the first place, reversed.
        var rows = section.Controls
            .Select((control, index) => (control, row: index / columns))
            .GroupBy(x => x.row, x => x.control)
            .Select(row => new XElement("row", row.Select(control => WriteCell(scopeKey, control, controlDescriptions))));

        element.Add(new XElement("rows", rows));
        return element;
    }

    private static XElement WriteHeaderOrFooter(string elementName, IReadOnlyList<FormControl> controls, List<XElement> controlDescriptions) => new(
        elementName,
        new XAttribute("id", DeterministicGuid(elementName)),
        new XElement("rows", controls.Select(control => new XElement("row", WriteCell(elementName, control, controlDescriptions)))));

    private static XElement WriteCell(string scopeKey, FormControl control, List<XElement> controlDescriptions)
    {
        var cell = new XElement("cell", new XAttribute("id", DeterministicGuid("cell", scopeKey, control.Id)));

        if (control.Visible == false)
        {
            cell.SetAttributeValue("visible", "false");
        }

        if (control.ColumnSpan is > 1)
        {
            cell.SetAttributeValue("colspan", control.ColumnSpan.Value);
        }

        if (control.RowSpan is > 1)
        {
            cell.SetAttributeValue("rowspan", control.RowSpan.Value);
        }

        if (control.Label is not null)
        {
            cell.Add(WriteLabels(control.Label));
        }

        var controlElement = new XElement("control", new XAttribute("id", control.Id));

        if (control.Field is not null)
        {
            controlElement.SetAttributeValue("datafieldname", control.Field);
        }

        // Control (a recognized standard control's friendly name, reversed
        // back to its classid) takes priority over CustomControlId, which
        // in turn takes priority over the legacy ClassId — see
        // StandardFormControls.Resolve's own doc comment. FormControlValidator
        // already confirmed Control names something recognized (and that
        // at most one of the three is actually set) before this ever runs,
        // so an unrecognized name silently produces no classid here rather
        // than throwing — the validator's job, not this writer's.
        if (StandardFormControls.Resolve(control) is { } resolvedClassId)
        {
            controlElement.SetAttributeValue("classid", resolvedClassId);
        }

        if (control.Disabled == true)
        {
            controlElement.SetAttributeValue("disabled", "true");
        }

        if (control.AdditionalControls is { Count: > 0 })
        {
            var uniqueId = DeterministicGuid("uniqueid", scopeKey, control.Id);
            controlElement.SetAttributeValue("uniqueid", uniqueId);
            controlDescriptions.Add(new XElement(
                "controlDescription",
                new XAttribute("forControl", uniqueId),
                control.AdditionalControls.Select(WriteAdditionalControl)));
        }

        if (control.Parameters is not null)
        {
            var parameters = new XElement("parameters");
            PopulateParameterElement(parameters, control.Parameters);
            controlElement.Add(parameters);
        }

        cell.Add(controlElement);

        if (control.Events is { Count: > 0 })
        {
            cell.Add(new XElement("events", control.Events.Select(WriteEvent)));
        }

        return cell;
    }

    private static XElement WriteAdditionalControl(FormAdditionalControl additional)
    {
        var element = new XElement("customControl");

        if (additional.Id is not null)
        {
            element.SetAttributeValue("id", additional.Id);
        }

        if (additional.Name is not null)
        {
            element.SetAttributeValue("name", additional.Name);
        }

        if (additional.FormFactor is not null)
        {
            element.SetAttributeValue("formFactor", additional.FormFactor.Value);
        }

        if (additional.Version is not null)
        {
            element.SetAttributeValue("version", additional.Version);
        }

        if (additional.Parameters is not null)
        {
            var parameters = new XElement("parameters");
            PopulateParameterElement(parameters, additional.Parameters);
            element.Add(parameters);
        }

        return element;
    }

    /// <summary>
    /// Reverses <see cref="FormJsonDefinitionReader"/>'s <c>ConvertToObject</c>:
    /// a plain string becomes the element's own text; a map's `attributes`
    /// key becomes XML attributes, its `value` key becomes the element's
    /// text, its `xml` key (see that method's own doc comment) rebuilds the
    /// wrapped fragment as a real element tree and re-escapes *that whole
    /// tree's own rendered text* as this element's value rather than adding
    /// it as a child, and every other key becomes a child element (repeated
    /// once per list entry when the value is a list) named after that key.
    /// A deliberately omitted `false` (see `docs/yaml-conventions.md` Rule 3)
    /// is never re-added — that omission was already equivalent to writing
    /// it, so there's nothing to restore.
    ///
    /// One confirmed, harmless side effect of that: a `&lt;parameters&gt;`
    /// whose only attribute was a stripped `false`, alongside plain text
    /// (e.g. a real "SalesPhoneNumberControl" `&lt;parameters
    /// isPreview="false"&gt;telephone1&lt;/parameters&gt;"), gets exported as
    /// `{ value: telephone1 }`; rebuilding it here (correctly, with no
    /// attribute left to write) produces `&lt;parameters&gt;telephone1&lt;/parameters&gt;`,
    /// which — if decomposed a *second* time — reads back as the plain
    /// string `telephone1` rather than `{ value: telephone1 }`. The value
    /// itself round-trips perfectly either way and the rebuilt FormXML is
    /// correct; only the wrapper shape normalises on a second pass, purely
    /// as a consequence of Rule 3 already being correct. Confirmed live:
    /// every non-dashboard form re-exported this session round-trips
    /// byte-identical except for this one pattern.
    /// </summary>
    private static void PopulateParameterElement(XElement element, object value)
    {
        if (value is string text)
        {
            element.Value = text;
            return;
        }

        if (value is not IDictionary map)
        {
            return;
        }

        foreach (DictionaryEntry entry in map)
        {
            var key = entry.Key.ToString()!;

            if (key == "attributes" && entry.Value is IDictionary attributes)
            {
                foreach (DictionaryEntry attribute in attributes)
                {
                    element.SetAttributeValue(attribute.Key.ToString()!, attribute.Value?.ToString());
                }

                continue;
            }

            if (key == "value" && entry.Value is string ownText)
            {
                element.Value = ownText;
                continue;
            }

            if (key == "xml" && entry.Value is IDictionary xmlWrapper)
            {
                // Reverses the reader's `xml` marker (see
                // FormJsonDefinitionReader.ConvertToObject's own doc
                // comment): build the fragment's own root element (there's
                // always exactly one entry — the fragment's root element
                // name) as a real XElement tree first, then set its
                // rendered text as this element's own value rather than
                // adding it as a real child — XElement escapes it back into
                // `&lt;.../&gt;` automatically when the whole document is
                // serialised, matching how Dataverse itself double-encodes
                // it (e.g. a quick view control's QuickForms).
                foreach (DictionaryEntry fragment in xmlWrapper)
                {
                    var fragmentRoot = new XElement(fragment.Key.ToString()!);
                    if (fragment.Value is not null)
                    {
                        PopulateParameterElement(fragmentRoot, fragment.Value);
                    }

                    element.Value = fragmentRoot.ToString(SaveOptions.DisableFormatting);
                }

                continue;
            }

            if (entry.Value is IList list)
            {
                foreach (var item in list)
                {
                    var child = new XElement(key);
                    PopulateParameterElement(child, item!);
                    element.Add(child);
                }
            }
            else if (entry.Value is not null)
            {
                var child = new XElement(key);
                PopulateParameterElement(child, entry.Value);
                element.Add(child);
            }
        }
    }

    private static XElement WriteEvent(FormEvent evt)
    {
        var element = new XElement("event");

        if (evt.Name is not null)
        {
            element.SetAttributeValue("name", evt.Name);
        }

        if (evt.Attribute is not null)
        {
            element.SetAttributeValue("attribute", evt.Attribute);
        }

        if (evt.Active is not null)
        {
            element.SetAttributeValue("active", evt.Active.Value ? "true" : "false");
        }

        if (evt.Handlers is { Count: > 0 })
        {
            element.Add(new XElement("Handlers", evt.Handlers.Select(handler => WriteEventHandler(handler, "handler"))));
        }

        if (evt.InternalHandlers is { Count: > 0 })
        {
            element.Add(new XElement("InternalHandlers", evt.InternalHandlers.Select(handler => WriteEventHandler(handler, "internal-handler"))));
        }

        return element;
    }

    private static XElement WriteEventHandler(FormEventHandler handler, string seedPrefix)
    {
        var element = new XElement(
            "Handler",
            new XAttribute("functionName", handler.FunctionName),
            new XAttribute("libraryName", handler.LibraryName),
            new XAttribute("handlerUniqueId", DeterministicGuid(seedPrefix, handler.FunctionName, handler.LibraryName)));

        if (handler.Enabled is not null)
        {
            element.SetAttributeValue("enabled", handler.Enabled.Value ? "true" : "false");
        }

        if (handler.PassExecutionContext is not null)
        {
            element.SetAttributeValue("passExecutionContext", handler.PassExecutionContext.Value ? "true" : "false");
        }

        return element;
    }

    private static XElement WriteLibrary(FormLibrary library) => new(
        "Library",
        new XAttribute("name", library.Name),
        new XAttribute("libraryUniqueId", DeterministicGuid("library", library.Name)));

    private static XElement WriteDisplayCondition(FormDisplayCondition condition)
    {
        var element = new XElement("DisplayConditions");

        if (condition.FallbackForm is not null)
        {
            element.SetAttributeValue("FallbackForm", condition.FallbackForm.Value ? "true" : "false");
        }

        if (condition.Order is not null)
        {
            element.SetAttributeValue("Order", condition.Order.Value);
        }

        if (condition.Roles is { Count: > 0 })
        {
            element.Add(condition.Roles.Select(id => new XElement("Role", new XAttribute("Id", id))));
        }
        else
        {
            element.Add(new XElement("Everyone"));
        }

        return element;
    }

    private static XElement WriteLabels(string label) => new(
        "labels",
        new XElement("label", new XAttribute("description", label), new XAttribute("languagecode", "1033")));

    /// <summary>
    /// Derives a stable GUID from human-authored data (names, ids) rather
    /// than <see cref="Guid.NewGuid"/> — this tool doesn't have the
    /// original tab/section/cell/library/handler ids to round-trip (it
    /// never captured them; see `docs/yaml-conventions.md`), but a random
    /// one every run would make re-running this on unchanged YAML produce
    /// spurious diffs. Same idea as a name-based UUID (RFC 4122 §4.3),
    /// though not byte-for-byte the same algorithm.
    /// </summary>
    private static string DeterministicGuid(params string[] seedParts)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(string.Join('/', seedParts)));
        return $"{{{new Guid(hash).ToString().ToUpperInvariant()}}}";
    }
}

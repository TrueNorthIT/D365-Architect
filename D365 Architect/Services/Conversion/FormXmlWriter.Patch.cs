using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Patching an existing form (issue #58). The live FormXML is the document;
/// the YAML only says what to change on it. Each component is matched to its
/// live counterpart — tabs and sections by name, cells by their control's id,
/// handlers by function name, libraries by name — and changed by a three-way
/// merge: render the component from the live form's own model and from the
/// YAML, and apply only the difference between those two renders onto the
/// live XML. Anything the model doesn't capture (a spacer cell, an empty row,
/// a section's <c>labelwidth</c>, a cell's <c>locklevel</c>, every id, a
/// default-valued attribute) renders identically on both sides, so it never
/// shows up in that difference and survives byte-for-byte.
///
/// What the YAML adds is inserted on its own: a new tab or section is written
/// out in full, a new field goes in a row of its own (appended when it's the
/// last field, otherwise after the field before it), so no existing cell
/// moves. A field the YAML moves elsewhere is carried over as its live cell,
/// leaving a spacer behind when a cell to its right would otherwise shift.
/// Anything live but missing from the YAML is kept unless
/// <c>allowRemovals</c> is set.
/// </summary>
public static partial class FormXmlWriter
{
    private static string Patch(FormDefinition yaml, XElement existingForm, bool allowRemovals) =>
        new FormPatcher(yaml, existingForm, allowRemovals).Run().ToString(SaveOptions.DisableFormatting);

    // ---- renders of each component, from either model ----

    /// <summary>The scope a patch renders every existing cell under, so its two renders get identical ids.</summary>
    private const string PatchScope = "patch";

    /// <summary>
    /// A control's cell as the writer renders it. When the live control has a
    /// <c>uniqueid</c>, that replaces the generated one (and every
    /// <c>controlDescription</c>'s <c>forControl</c>), so the render points at
    /// the same control the live form does. A YAML control without a label
    /// leaves the live label alone: Dataverse fills a bound control's label
    /// from the column's display name when it saves a form (confirmed live: a
    /// control written without <c>label</c> reads back with
    /// <c>&lt;labels&gt;&lt;label description="Invoice Centre Name" languagecode="1033" /&gt;&lt;/labels&gt;</c>),
    /// so a file that leaves <c>label</c> out has nothing to compare.
    /// </summary>
    internal static (XElement Cell, List<XElement> ControlDescriptions) RenderCell(string scopeKey, FormControl control, string? liveUniqueId, bool dropLabels = false)
    {
        var controlDescriptions = new List<XElement>();
        var cell = WriteCell(scopeKey, control, controlDescriptions);

        if (liveUniqueId is not null)
        {
            var controlElement = cell.Element("control")!;
            if (controlElement.Attribute("uniqueid") is not null)
            {
                controlElement.SetAttributeValue("uniqueid", liveUniqueId);
            }

            controlDescriptions.ForEach(description => description.SetAttributeValue("forControl", liveUniqueId));
        }

        if (dropLabels)
        {
            cell.Element("labels")?.Remove();
        }

        return (cell, controlDescriptions);
    }

    /// <summary>A tab as written, without its columns: the part of it that's the tab's own.</summary>
    internal static XElement RenderTabShell(FormTab tab) => Without(WriteTab(tab, []), "columns");

    /// <summary>A column as written, without its sections.</summary>
    internal static XElement RenderColumnShell(string tabScopeKey, FormColumn column) => Without(WriteColumn(tabScopeKey, column, []), "sections");

    /// <summary>A section as written, without its rows.</summary>
    internal static XElement RenderSectionShell(string tabScopeKey, FormSection section) => Without(WriteSection(tabScopeKey, section, []), "rows");

    /// <summary>
    /// The form-level blocks a patch merges as wholes, rendered from a model:
    /// null when the model has none. Each is keyed by its element name.
    /// </summary>
    internal static IReadOnlyDictionary<string, XElement?> RenderBlocks(FormDefinition form) => new Dictionary<string, XElement?>
    {
        ["ancestor"] = form.Ancestor is not null ? new XElement("ancestor", new XAttribute("id", form.Ancestor)) : null,
        ["hiddencontrols"] = form.HiddenFields is { Count: > 0 } ? new XElement("hiddencontrols", form.HiddenFields.Select(WriteHiddenField)) : null,
        ["events"] = form.Events is { Count: > 0 } ? new XElement("events", form.Events.Select(WriteEvent)) : null,
        ["formLibraries"] = form.Libraries is { Count: > 0 } ? new XElement("formLibraries", form.Libraries.Select(WriteLibrary)) : null,
        ["DisplayConditions"] = form.DisplayCondition is not null ? WriteDisplayCondition(form.DisplayCondition) : null,
    };

    internal static string TabKey(FormTab tab) => tab.Name ?? tab.Label ?? "tab";

    internal static string SectionKey(FormSection section) => section.Name ?? section.Label ?? "section";

    private static XElement Without(XElement element, string childName)
    {
        element.Element(childName)?.Remove();
        return element;
    }

    /// <summary>
    /// Applies what changed between <paramref name="baseRender"/> (the live
    /// model's render) and <paramref name="newRender"/> (the YAML's) onto
    /// <paramref name="live"/>, matching children the way
    /// <see cref="FormXmlComparer"/> does. Whatever is on the live element but
    /// in neither render is never touched. A child the YAML dropped is removed
    /// only when <paramref name="removeMissing"/> is set.
    /// </summary>
    private static void Merge(XElement live, XElement baseRender, XElement newRender, bool removeMissing)
    {
        var attributeNames = baseRender.Attributes().Concat(newRender.Attributes())
            .Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name).Distinct();
        foreach (var name in attributeNames)
        {
            var after = (string?)newRender.Attribute(name);
            if ((string?)baseRender.Attribute(name) != after)
            {
                live.SetAttributeValue(name, after);
            }
        }

        if (!baseRender.HasElements && !newRender.HasElements)
        {
            var text = FormXmlComparer.TextOf(newRender);
            if (FormXmlComparer.TextOf(baseRender) != text && !live.HasElements)
            {
                live.Value = text;
            }

            return;
        }

        var baseChildren = FormXmlComparer.SegmentsOf(baseRender).ToDictionary(c => c.Segment, c => c.Element);
        var liveChildren = FormXmlComparer.SegmentsOf(live).ToDictionary(c => c.Segment, c => c.Element);
        var newChildren = FormXmlComparer.SegmentsOf(newRender);

        XElement? previous = null;
        foreach (var (segment, newChild) in newChildren)
        {
            liveChildren.TryGetValue(segment, out var liveChild);
            if (baseChildren.TryGetValue(segment, out var baseChild))
            {
                if (liveChild is not null)
                {
                    Merge(liveChild, baseChild, newChild, removeMissing);
                    previous = liveChild;
                }
                else if (!XNode.DeepEquals(baseChild, newChild))
                {
                    previous = InsertAfter(live, previous, new XElement(newChild));
                }
            }
            else if (liveChild is not null)
            {
                Merge(liveChild, new XElement(newChild.Name), newChild, removeMissing);
                previous = liveChild;
            }
            else
            {
                previous = InsertAfter(live, previous, new XElement(newChild));
            }
        }

        if (removeMissing)
        {
            var kept = newChildren.Select(c => c.Segment).ToHashSet();
            foreach (var segment in baseChildren.Keys.Where(s => !kept.Contains(s)))
            {
                liveChildren.GetValueOrDefault(segment)?.Remove();
            }
        }
    }

    private static XElement InsertAfter(XElement parent, XElement? previous, XElement child)
    {
        if (previous is not null && previous.Parent == parent)
        {
            previous.AddAfterSelf(child);
        }
        else
        {
            parent.AddFirst(child);
        }

        return child;
    }

    private sealed class FormPatcher
    {
        private readonly FormDefinition _yaml;
        private readonly FormDefinition _liveModel;
        private readonly XElement _form;
        private readonly bool _allowRemovals;

        private readonly Dictionary<string, (XElement Element, FormTab Model)> _liveTabs = [];
        // Section names repeat across tabs on real forms (two tabs each with a
        // "tab_12_section_1"), so a section is matched within its own tab
        // first, and anywhere on the form only when its name is unique.
        private readonly Dictionary<string, (XElement Element, FormSection Model)> _liveSections = [];
        private readonly Dictionary<string, List<(XElement Element, FormSection Model)>> _liveSectionsByName = [];
        // Control ids repeat on real forms too (a field shown twice, both
        // controls named after it), so cells are listed per id in document
        // order and matched by occurrence: the YAML's first "name" is the
        // form's first "name". Each live cell keeps the model it was read as.
        private readonly Dictionary<XElement, FormControl> _liveCellModels = [];
        private readonly Dictionary<string, List<XElement>> _bodyCells = [];
        private readonly Dictionary<string, List<XElement>> _headerCells = [];
        private readonly Dictionary<string, List<XElement>> _footerCells = [];

        private readonly HashSet<XElement> _placed = [];
        private readonly Dictionary<XElement, List<XElement>> _newControlDescriptions = [];

        public FormPatcher(FormDefinition yaml, XElement existingForm, bool allowRemovals)
        {
            _yaml = yaml;
            _allowRemovals = allowRemovals;
            _form = new XElement(existingForm);
            _liveModel = FormJsonDefinitionReader.ReadFormXml(existingForm);
            Index();
        }

        private void Index()
        {
            // The reader maps tabs/columns/sections in document order, so the
            // live model lines up with the live elements position by position.
            var tabElements = _form.Element("tabs")?.Elements("tab").ToList() ?? [];
            foreach (var (tabElement, tab) in tabElements.Zip(_liveModel.Tabs))
            {
                _liveTabs.TryAdd(TabKey(tab), (tabElement, tab));

                var columnElements = tabElement.Element("columns")?.Elements("column").ToList() ?? [];
                foreach (var (columnElement, column) in columnElements.Zip(tab.Columns))
                {
                    var sectionElements = columnElement.Element("sections")?.Elements("section").ToList() ?? [];
                    foreach (var (sectionElement, section) in sectionElements.Zip(column.Sections))
                    {
                        _liveSections.TryAdd($"{TabKey(tab)}/{SectionKey(section)}", (sectionElement, section));
                        (_liveSectionsByName.TryGetValue(SectionKey(section), out var sameName) ? sameName : _liveSectionsByName[SectionKey(section)] = []).Add((sectionElement, section));
                    }
                }
            }

            IndexCells(_form.Element("tabs"), BodyControls(_liveModel), _bodyCells);
            IndexCells(_form.Element("header"), _liveModel.HeaderControls ?? [], _headerCells);
            IndexCells(_form.Element("footer"), _liveModel.FooterControls ?? [], _footerCells);
        }

        private void IndexCells(XElement? container, IEnumerable<FormControl> models, Dictionary<string, List<XElement>> cells)
        {
            foreach (var control in container?.Descendants("control") ?? [])
            {
                if ((string?)control.Attribute("id") is { } id && control.Parent?.Name == "cell")
                {
                    (cells.TryGetValue(id, out var list) ? list : cells[id] = []).Add(control.Parent);
                }
            }

            foreach (var sameId in models.GroupBy(m => m.Id))
            {
                foreach (var (cell, model) in (cells.GetValueOrDefault(sameId.Key) ?? []).Zip(sameId))
                {
                    _liveCellModels[cell] = model;
                }
            }
        }

        /// <summary>The first live cell for <paramref name="controlId"/> not yet matched to the YAML.</summary>
        private XElement? NextLiveCell(Dictionary<string, List<XElement>> cells, string controlId) =>
            cells.GetValueOrDefault(controlId)?.FirstOrDefault(cell => !_placed.Contains(cell));

        public XElement Run()
        {
            MergeTabs();
            MergeArea("header", _yaml.HeaderControls, _headerCells);
            MergeArea("footer", _yaml.FooterControls, _footerCells);
            MergeBlocks();
            MergeControlDescriptions();

            if (_allowRemovals)
            {
                RemoveWhatTheYamlDropped();
            }

            return _form;
        }

        // ---- tabs, columns, sections ----

        private void MergeTabs()
        {
            var tabsElement = _form.Element("tabs") ?? AddTopLevel(new XElement("tabs"));
            XElement? previous = null;

            foreach (var tab in _yaml.Tabs)
            {
                if (_liveTabs.TryGetValue(TabKey(tab), out var live) && _placed.Add(live.Element))
                {
                    Merge(live.Element, RenderTabShell(live.Model), RenderTabShell(tab), removeMissing: true);
                    PlaceAfter(live.Element, previous, tabsElement);
                    MergeColumns(live.Element, live.Model, tab);
                    previous = live.Element;
                }
                else
                {
                    var element = RenderTabShell(tab);
                    element.Add(new XElement("columns"));
                    InsertAfter(tabsElement, previous, element);
                    _placed.Add(element);
                    MergeColumns(element, liveModel: null, tab);
                    previous = element;
                }
            }
        }

        private void MergeColumns(XElement tabElement, FormTab? liveModel, FormTab tab)
        {
            var columnsElement = tabElement.Element("columns") ?? AddChild(tabElement, new XElement("columns"));
            var liveColumns = columnsElement.Elements("column").ToList();
            var tabScopeKey = TabKey(tab);

            for (var i = 0; i < tab.Columns.Count; i++)
            {
                var column = tab.Columns[i];
                XElement columnElement;
                if (i < liveColumns.Count)
                {
                    columnElement = liveColumns[i];
                    var liveColumn = liveModel is not null && i < liveModel.Columns.Count ? liveModel.Columns[i] : null;
                    var baseRender = liveColumn is not null ? RenderColumnShell(tabScopeKey, liveColumn) : new XElement("column");
                    Merge(columnElement, baseRender, RenderColumnShell(tabScopeKey, column), removeMissing: true);
                }
                else
                {
                    columnElement = RenderColumnShell(tabScopeKey, column);
                    columnElement.Add(new XElement("sections"));
                    columnsElement.Add(columnElement);
                }

                MergeSections(columnElement, column, tabScopeKey);
            }
        }

        private void MergeSections(XElement columnElement, FormColumn column, string tabScopeKey)
        {
            var sectionsElement = columnElement.Element("sections") ?? AddChild(columnElement, new XElement("sections"));
            XElement? previous = null;

            foreach (var section in column.Sections)
            {
                XElement sectionElement;
                FormSection? liveModel = null;
                if (FindLiveSection(tabScopeKey, section) is { } live && _placed.Add(live.Element))
                {
                    sectionElement = live.Element;
                    liveModel = live.Model;
                    Merge(sectionElement, RenderSectionShell(tabScopeKey, live.Model), RenderSectionShell(tabScopeKey, section), removeMissing: true);
                    PlaceAfter(sectionElement, previous, sectionsElement);
                    MergeControls(sectionElement.Element("rows") ?? AddChild(sectionElement, new XElement("rows")), section.Controls, $"{tabScopeKey}/{SectionKey(section)}", _bodyCells);
                }
                else
                {
                    // A new section is written out in full, so its fields are
                    // laid out across its columns as the writer always has;
                    // any field it takes over from elsewhere on the form keeps
                    // its live cell.
                    var collected = new List<XElement>();
                    sectionElement = WriteSection(tabScopeKey, section, collected);
                    InsertAfter(sectionsElement, previous, sectionElement);
                    _placed.Add(sectionElement);
                    RecordControlDescriptions(sectionElement, collected);
                    AdoptLiveCells(sectionElement, section);
                }

                previous = sectionElement;
            }
        }

        private (XElement Element, FormSection Model)? FindLiveSection(string tabScopeKey, FormSection section)
        {
            if (_liveSections.TryGetValue($"{tabScopeKey}/{SectionKey(section)}", out var inThisTab) && !_placed.Contains(inThisTab.Element))
            {
                return inThisTab;
            }

            return _liveSectionsByName.TryGetValue(SectionKey(section), out var sameName) && sameName.Count == 1 ? sameName[0] : null;
        }

        // ---- cells ----

        private void MergeControls(XElement rowsElement, IReadOnlyList<FormControl> controls, string scopeKey, Dictionary<string, List<XElement>> liveCells)
        {
            XElement? previousCell = null;

            for (var i = 0; i < controls.Count; i++)
            {
                var control = controls[i];
                var isLastPresentHere = !controls.Skip(i + 1).Any(later => (liveCells.GetValueOrDefault(later.Id) ?? [])
                    .Any(laterCell => !_placed.Contains(laterCell) && laterCell.Ancestors("rows").FirstOrDefault() == rowsElement));

                if (NextLiveCell(liveCells, control.Id) is { } cell && _placed.Add(cell))
                {
                    var inThisSection = cell.Ancestors("rows").FirstOrDefault() == rowsElement;
                    if (!inThisSection || (previousCell is not null && cell.IsBefore(previousCell)))
                    {
                        Detach(cell);
                        InsertInOwnRow(cell, previousCell, rowsElement, isLastPresentHere);
                    }

                    MergeCell(cell, control);
                    previousCell = cell;
                }
                else
                {
                    var (newCell, descriptions) = RenderCell(scopeKey, control, liveUniqueId: null);
                    _newControlDescriptions[newCell] = descriptions;
                    InsertInOwnRow(newCell, previousCell, rowsElement, isLastPresentHere);
                    _placed.Add(newCell);
                    previousCell = newCell;
                }
            }
        }

        private void MergeCell(XElement cell, FormControl control)
        {
            var liveUniqueId = (string?)cell.Element("control")?.Attribute("uniqueid");
            var dropLabels = control.Label is null;
            var (baseRender, _) = RenderCell(PatchScope, _liveCellModels.GetValueOrDefault(cell) ?? control, liveUniqueId, dropLabels);
            var (newRender, descriptions) = RenderCell(PatchScope, control, liveUniqueId, dropLabels);
            _newControlDescriptions[cell] = descriptions;
            Merge(cell, baseRender, newRender, removeMissing: true);
        }

        /// <summary>
        /// In a freshly written section, swaps every cell for a field that's
        /// already on the form for that field's live cell.
        /// </summary>
        private void AdoptLiveCells(XElement newSection, FormSection section)
        {
            // WriteSection lays the section's controls out in their own order,
            // one cell each, so rendered cells and controls line up.
            foreach (var (renderedCell, control) in newSection.Descendants("cell").ToList().Zip(section.Controls))
            {
                if (NextLiveCell(_bodyCells, control.Id) is not { } liveCell || !_placed.Add(liveCell))
                {
                    continue;
                }

                _newControlDescriptions.Remove(renderedCell);
                Detach(liveCell);
                renderedCell.ReplaceWith(liveCell);
                MergeCell(liveCell, control);
            }

            _placed.UnionWith(newSection.Descendants("cell"));
        }

        /// <summary>
        /// Takes a cell out of its row. A cell to its right would slide left
        /// into its place, so a spacer is left behind when there is one.
        /// </summary>
        private static void Detach(XElement cell)
        {
            if (cell.ElementsAfterSelf("cell").Any())
            {
                var controlId = (string?)cell.Element("control")?.Attribute("id") ?? "";
                cell.ReplaceWith(new XElement("cell",
                    new XAttribute("id", DeterministicGuid("spacer", controlId)),
                    new XAttribute("showlabel", "false"),
                    new XElement("labels", new XElement("label", new XAttribute("description", ""), new XAttribute("languagecode", 1033)))));
            }
            else
            {
                cell.Remove();
            }
        }

        private static void InsertInOwnRow(XElement cell, XElement? previousCell, XElement rowsElement, bool append)
        {
            var row = new XElement("row", cell);
            if (append || (previousCell is null && !rowsElement.HasElements))
            {
                rowsElement.Add(row);
            }
            else if (previousCell?.Parent is { } previousRow && previousRow.Parent == rowsElement)
            {
                previousRow.AddAfterSelf(row);
            }
            else
            {
                rowsElement.AddFirst(row);
            }
        }

        // ---- header and footer ----

        private void MergeArea(string name, IReadOnlyList<FormControl>? controls, Dictionary<string, List<XElement>> liveCells)
        {
            if (controls is not { Count: > 0 })
            {
                return;
            }

            var area = _form.Element(name);
            if (area is null)
            {
                var collected = new List<XElement>();
                area = WriteHeaderOrFooter(name, controls, collected);
                AddTopLevel(area);
                RecordControlDescriptions(area, collected);
                return;
            }

            MergeControls(area.Element("rows") ?? AddChild(area, new XElement("rows")), controls, name, liveCells);
        }

        // ---- form-level blocks ----

        private void MergeBlocks()
        {
            var baseBlocks = RenderBlocks(_liveModel);
            foreach (var (name, newBlock) in RenderBlocks(_yaml))
            {
                var baseBlock = baseBlocks[name];
                var live = _form.Element(name);

                if (newBlock is null)
                {
                    if (baseBlock is not null && _allowRemovals)
                    {
                        live?.Remove();
                    }

                    continue;
                }

                if (live is null)
                {
                    AddTopLevel(newBlock);
                    continue;
                }

                // Display conditions are one setting, not a list of things to
                // keep: switching to "everyone" must drop the roles.
                Merge(live, baseBlock ?? new XElement(name), newBlock, removeMissing: _allowRemovals || name == "DisplayConditions");
            }
        }

        private void MergeControlDescriptions()
        {
            List<XElement> BaseDescriptions(KeyValuePair<XElement, FormControl> live) =>
                RenderCell(PatchScope, live.Value, (string?)live.Key.Element("control")?.Attribute("uniqueid")).ControlDescriptions;

            var baseDescriptions = _liveCellModels.SelectMany(BaseDescriptions).ToList();
            var newDescriptions = _newControlDescriptions.Values.SelectMany(d => d)
                .Concat(_allowRemovals ? [] : _liveCellModels.Where(live => !_placed.Contains(live.Key)).SelectMany(BaseDescriptions))
                .ToList();

            if (baseDescriptions.Count == 0 && newDescriptions.Count == 0)
            {
                return;
            }

            var live = _form.Element("controlDescriptions");
            if (live is null)
            {
                AddTopLevel(new XElement("controlDescriptions", newDescriptions));
                return;
            }

            Merge(live, new XElement("controlDescriptions", baseDescriptions), new XElement("controlDescriptions", newDescriptions), removeMissing: true);
        }

        private void RecordControlDescriptions(XElement rendered, List<XElement> descriptions)
        {
            foreach (var description in descriptions)
            {
                var forControl = (string?)description.Attribute("forControl");
                if (rendered.Descendants("control").FirstOrDefault(c => (string?)c.Attribute("uniqueid") == forControl)?.Parent is { } cell)
                {
                    (_newControlDescriptions.TryGetValue(cell, out var list) ? list : _newControlDescriptions[cell] = []).Add(description);
                }
            }
        }

        // ---- removals (allowRemovals only) ----

        private void RemoveWhatTheYamlDropped()
        {
            foreach (var cell in _liveCellModels.Keys.Where(cell => !_placed.Contains(cell) && cell.Parent is not null))
            {
                Detach(cell);
            }

            foreach (var (_, (element, _)) in _liveSections.Where(s => !_placed.Contains(s.Value.Element)))
            {
                element.Remove();
            }

            foreach (var (_, (element, _)) in _liveTabs.Where(t => !_placed.Contains(t.Value.Element)))
            {
                element.Remove();
            }
        }

        // ---- helpers ----

        private static void PlaceAfter(XElement element, XElement? previous, XElement container)
        {
            if (element.Parent != container)
            {
                element.Remove();
                InsertAfter(container, previous, element);
            }
            else if (previous is not null && previous.Parent == container && element.IsBefore(previous))
            {
                element.Remove();
                previous.AddAfterSelf(element);
            }
        }

        private XElement AddTopLevel(XElement element)
        {
            _form.Add(element);
            return element;
        }

        private static XElement AddChild(XElement parent, XElement child)
        {
            parent.Add(child);
            return child;
        }
    }

    internal static IEnumerable<FormControl> BodyControls(FormDefinition form) =>
        form.Tabs.SelectMany(t => t.Columns).SelectMany(c => c.Sections).SelectMany(s => s.Controls);

    internal static IEnumerable<FormControl> AllControls(FormDefinition form) =>
        BodyControls(form).Concat(form.HeaderControls ?? []).Concat(form.FooterControls ?? []);
}

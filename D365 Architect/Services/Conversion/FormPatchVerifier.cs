using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Checks what a patch changed in the FormXML against what the YAML asked for
/// (issue #58), so `form import` can refuse a write that would change
/// something nobody asked it to.
///
/// What the YAML changed is worked out on the model: a control, section, tab,
/// header/footer or form-level block whose render differs between the live
/// form's model and the YAML's — or that is new, moved or gone. Each
/// difference <see cref="FormXmlComparer"/> finds is then attributed to the
/// nearest of those components that contains it: inside a changed one it's
/// <see cref="FormPatchReport.Expected"/>, anywhere else
/// <see cref="FormPatchReport.Unexpected"/> (a dropped spacer in an untouched
/// section, a regenerated id, a changed header the YAML left alone).
/// <see cref="FormPatchReport.WouldRemove"/> lists what is live but missing
/// from the YAML and not simply moved — kept unless removals are allowed.
/// </summary>
public static class FormPatchVerifier
{
    private static readonly string[] Blocks = ["ancestor", "hiddencontrols", "events", "formLibraries", "DisplayConditions"];

    public static FormPatchReport Verify(XElement liveForm, XElement newForm, FormDefinition liveModel, FormDefinition yamlModel)
    {
        var changes = new Changes(liveModel, yamlModel);
        var expected = new List<FormXmlDifference>();
        var unexpected = new List<FormXmlDifference>();

        foreach (var difference in FormXmlComparer.Compare(liveForm, newForm))
        {
            var (document, other) = difference.Kind == FormXmlDifferenceKind.ElementAdded ? (newForm, liveForm) : (liveForm, newForm);
            var element = Locate(document, difference.Path) ?? Locate(other, difference.Path);
            (element is not null && IsExpected(element, changes, liveForm, newForm) ? expected : unexpected).Add(difference);
        }

        return new FormPatchReport(expected, unexpected, changes.WouldRemove);
    }

    private static bool IsExpected(XElement element, Changes changes, XElement liveForm, XElement newForm)
    {
        var document = element.AncestorsAndSelf().Last();
        var other = document == liveForm ? newForm : liveForm;

        foreach (var e in element.AncestorsAndSelf())
        {
            switch (e.Name.LocalName)
            {
                case "cell" when e.Element("control") is { } control:
                    var id = (string?)control.Attribute("id") ?? "";
                    if (changes.Controls.Contains(id))
                    {
                        return true;
                    }

                    // A cell whose position now holds a different field has
                    // only shifted because the YAML changed its section; any
                    // real change to this field itself shows on its own.
                    var counterpart = Locate(other, PathOf(e));
                    if ((string?)counterpart?.Element("control")?.Attribute("id") != id && SectionOf(e) is { } section && changes.Sections.Contains(SectionKeyOf(section)))
                    {
                        break;
                    }

                    return false;
                case "section":
                    return changes.Sections.Contains(SectionKeyOf(e));
                case "tab":
                    return changes.Tabs.Contains(TabKeyOf(e));
                case "header" or "footer":
                    return changes.Areas.Contains(e.Name.LocalName);
                case "controlDescription":
                    var forControl = (string?)e.Attribute("forControl");
                    var described = liveForm.Descendants("control").Concat(newForm.Descendants("control"))
                        .FirstOrDefault(c => (string?)c.Attribute("uniqueid") == forControl);
                    return described is not null && changes.Controls.Contains((string?)described.Attribute("id") ?? "");
                case var name when Blocks.Contains(name) && e.Parent == document:
                    return changes.Blocks.Contains(name);
            }
        }

        return false;
    }

    // ---- locating a difference ----

    /// <summary>The element a difference's path points at (its attribute part ignored), or null.</summary>
    private static XElement? Locate(XElement root, string path)
    {
        var current = root;
        foreach (var segment in SplitPath(path).Where(s => !s.StartsWith('@')))
        {
            current = FormXmlComparer.SegmentsOf(current).FirstOrDefault(c => c.Segment == segment).Element;
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>Splits a path on <c>/</c>, except inside a key: <c>Library[name=tn_/lib.js]</c> is one segment.</summary>
    private static IEnumerable<string> SplitPath(string path)
    {
        var start = 0;
        var depth = 0;
        for (var i = 0; i < path.Length; i++)
        {
            depth += path[i] switch { '[' => 1, ']' => -1, _ => 0 };
            if (path[i] == '/' && depth == 0)
            {
                yield return path[start..i];
                start = i + 1;
            }
        }

        yield return path[start..];
    }

    private static string PathOf(XElement element) => string.Join('/', element.AncestorsAndSelf().TakeWhile(e => e.Parent is not null).Reverse()
        .Select(e => FormXmlComparer.SegmentsOf(e.Parent!).First(c => c.Element == e).Segment));

    private static XElement? SectionOf(XElement element) => element.Ancestors("section").FirstOrDefault();

    private static string TabKeyOf(XElement tab) =>
        (string?)tab.Attribute("name") ?? (string?)tab.Element("labels")?.Element("label")?.Attribute("description") ?? "tab";

    private static string SectionKeyOf(XElement section)
    {
        var name = (string?)section.Attribute("name") ?? (string?)section.Element("labels")?.Element("label")?.Attribute("description") ?? "section";
        return section.Ancestors("tab").FirstOrDefault() is { } tab ? $"{TabKeyOf(tab)}/{name}" : name;
    }

    // ---- what the YAML changed ----

    private sealed class Changes
    {
        public HashSet<string> Controls { get; } = [];
        public HashSet<string> Sections { get; } = [];
        public HashSet<string> Tabs { get; } = [];
        public HashSet<string> Areas { get; } = [];
        public HashSet<string> Blocks { get; } = [];
        public List<string> WouldRemove { get; } = [];

        public Changes(FormDefinition live, FormDefinition yaml)
        {
            CompareTabs(live, yaml);
            CompareControls(live, yaml);
            CompareArea("header", live.HeaderControls, yaml.HeaderControls);
            CompareArea("footer", live.FooterControls, yaml.FooterControls);
            CompareBlocks(live, yaml);
        }

        private void CompareTabs(FormDefinition live, FormDefinition yaml)
        {
            var liveTabs = live.Tabs.GroupBy(FormXmlWriter.TabKey).ToDictionary(g => g.Key, g => g.First());
            var yamlTabs = yaml.Tabs.GroupBy(FormXmlWriter.TabKey).ToDictionary(g => g.Key, g => g.First());

            var liveOrder = live.Tabs.Select(FormXmlWriter.TabKey).Where(yamlTabs.ContainsKey).ToList();
            var yamlOrder = yaml.Tabs.Select(FormXmlWriter.TabKey).Where(liveTabs.ContainsKey).ToList();
            var reordered = !liveOrder.SequenceEqual(yamlOrder);

            foreach (var (key, tab) in yamlTabs)
            {
                if (!liveTabs.TryGetValue(key, out var liveTab) || reordered || !SameTab(liveTab, tab))
                {
                    Tabs.Add(key);
                }
            }

            foreach (var key in liveTabs.Keys.Where(k => !yamlTabs.ContainsKey(k)))
            {
                Tabs.Add(key);
                WouldRemove.Add($"tab '{key}'");
            }

            var liveSections = SectionsOf(live);
            var yamlSections = SectionsOf(yaml);
            var yamlSectionNames = yamlSections.Select(s => s.Name).ToHashSet();

            foreach (var (key, name, section) in yamlSections)
            {
                var match = liveSections.FirstOrDefault(s => s.Key == key);
                match = match.Section is null ? liveSections.Where(s => s.Name == name).ToList() is [var only] ? only : default : match;
                if (match.Section is null)
                {
                    Sections.Add(key);
                    continue;
                }

                if (match.Key != key || !SameSection(match.Key, match.Section, section))
                {
                    Sections.Add(key);
                    Sections.Add(match.Key);
                }
            }

            foreach (var (key, name, _) in liveSections.Where(s => !yamlSectionNames.Contains(s.Name)))
            {
                Sections.Add(key);
                WouldRemove.Add($"section '{name}' (tab '{key[..^(name.Length + 1)]}')");
            }
        }

        private void CompareControls(FormDefinition live, FormDefinition yaml)
        {
            var liveControls = ControlsOf(live).ToList();
            var yamlControls = ControlsOf(yaml).ToList();
            var yamlIds = yamlControls.Select(c => c.Control.Id).ToHashSet();

            // Matched by id and occurrence, as the patcher matches cells.
            foreach (var sameId in yamlControls.GroupBy(c => c.Control.Id))
            {
                var liveSameId = liveControls.Where(c => c.Control.Id == sameId.Key).ToList();
                foreach (var (yamlControl, index) in sameId.Select((c, i) => (c, i)))
                {
                    if (index >= liveSameId.Count || liveSameId[index].Container != yamlControl.Container || !SameControl(liveSameId[index].Control, yamlControl.Control))
                    {
                        Controls.Add(sameId.Key);
                    }
                }
            }

            foreach (var (control, container) in liveControls.Where(c => !yamlIds.Contains(c.Control.Id)))
            {
                Controls.Add(control.Id);
                WouldRemove.Add(container is "header" or "footer" ? $"{container} control '{control.Id}'" : $"control '{control.Id}' (section '{container}')");
            }
        }

        private void CompareArea(string name, IReadOnlyList<FormControl>? live, IReadOnlyList<FormControl>? yaml)
        {
            if (!(live ?? []).Select(c => c.Id).SequenceEqual((yaml ?? []).Select(c => c.Id)))
            {
                Areas.Add(name);
            }
        }

        private void CompareBlocks(FormDefinition live, FormDefinition yaml)
        {
            var liveBlocks = FormXmlWriter.RenderBlocks(live);
            var yamlBlocks = FormXmlWriter.RenderBlocks(yaml);
            foreach (var name in FormPatchVerifier.Blocks)
            {
                if (!XNode.DeepEquals(liveBlocks[name], yamlBlocks[name]))
                {
                    Blocks.Add(name);
                }
            }

            WouldRemove.AddRange((live.Libraries ?? []).Select(l => l.Name).Except((yaml.Libraries ?? []).Select(l => l.Name)).Select(n => $"library '{n}'"));
            WouldRemove.AddRange((live.HiddenFields ?? []).Select(f => f.Field).Except((yaml.HiddenFields ?? []).Select(f => f.Field)).Select(f => $"hidden field '{f}'"));
            WouldRemove.AddRange(HandlersOf(live).Except(HandlersOf(yaml)).Select(h => $"handler '{h}'"));
        }

        private static IEnumerable<string> HandlersOf(FormDefinition form) =>
            (form.Events ?? []).SelectMany(e => (e.Handlers ?? []).Concat(e.InternalHandlers ?? []).Select(h => $"{h.FunctionName}' on '{e.Name}"));

        private static bool SameTab(FormTab live, FormTab yaml) =>
            XNode.DeepEquals(FormXmlWriter.RenderTabShell(live), FormXmlWriter.RenderTabShell(yaml))
            && live.Columns.Count == yaml.Columns.Count
            && live.Columns.Zip(yaml.Columns).All(pair =>
                XNode.DeepEquals(FormXmlWriter.RenderColumnShell("", pair.First), FormXmlWriter.RenderColumnShell("", pair.Second))
                && pair.First.Sections.Select(FormXmlWriter.SectionKey).SequenceEqual(pair.Second.Sections.Select(FormXmlWriter.SectionKey)));

        private static bool SameSection(string key, FormSection live, FormSection yaml) =>
            XNode.DeepEquals(FormXmlWriter.RenderSectionShell(key, live), FormXmlWriter.RenderSectionShell(key, yaml))
            && live.Controls.Select(c => c.Id).SequenceEqual(yaml.Controls.Select(c => c.Id));

        private static bool SameControl(FormControl live, FormControl yaml)
        {
            var dropLabels = yaml.Label is null;
            return XNode.DeepEquals(FormXmlWriter.RenderCell("verify", live, null, dropLabels).Cell, FormXmlWriter.RenderCell("verify", yaml, null, dropLabels).Cell);
        }

        private static List<(string Key, string Name, FormSection Section)> SectionsOf(FormDefinition form) =>
            form.Tabs.SelectMany(tab => tab.Columns.SelectMany(column => column.Sections)
                .Select(section => ($"{FormXmlWriter.TabKey(tab)}/{FormXmlWriter.SectionKey(section)}", FormXmlWriter.SectionKey(section), section))).ToList();

        /// <summary>Every control with where it sits: its section ("tab/section"), or "header"/"footer".</summary>
        private static IEnumerable<(FormControl Control, string Container)> ControlsOf(FormDefinition form) =>
            SectionsOf(form).SelectMany(s => s.Section.Controls.Select(c => (c, s.Key)))
                .Concat((form.HeaderControls ?? []).Select(c => (c, "header")))
                .Concat((form.FooterControls ?? []).Select(c => (c, "footer")));
    }
}

public sealed record FormPatchReport(
    IReadOnlyList<FormXmlDifference> Expected,
    IReadOnlyList<FormXmlDifference> Unexpected,
    IReadOnlyList<string> WouldRemove);

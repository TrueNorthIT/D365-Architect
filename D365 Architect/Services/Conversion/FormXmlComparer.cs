using System.Xml.Linq;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reports every difference between two <c>&lt;form&gt;</c> elements, so a
/// rebuild can be proved to have changed nothing it wasn't asked to (issues
/// #58/#59). Only what is provably meaningless is ignored: whitespace-only
/// text, attribute order, namespace declarations and the XML declaration.
/// Everything else counts — including a dropped attribute that only restated
/// Dataverse's default, a GUID in another case, and an empty spacer cell or
/// row — because those are exactly what #58 lost without anyone noticing.
/// Values are compared case-sensitively.
///
/// Children are matched by key where the element has a stable one (see
/// <see cref="KeyOf"/>), otherwise by 1-based position among same-named
/// siblings, so a removed spacer cell shows up as one removal rather than as
/// every later cell in the row having changed.
/// </summary>
public static class FormXmlComparer
{
    public static IReadOnlyList<FormXmlDifference> Compare(XElement before, XElement after)
    {
        var differences = new List<FormXmlDifference>();
        CompareElement(before, after, path: "", differences);
        return differences;
    }

    private static void CompareElement(XElement before, XElement after, string path, List<FormXmlDifference> differences)
    {
        CompareAttributes(before, after, path, differences);

        if (!before.HasElements && !after.HasElements)
        {
            var beforeText = TextOf(before);
            var afterText = TextOf(after);
            if (beforeText != afterText)
            {
                differences.Add(new FormXmlDifference(path, FormXmlDifferenceKind.TextChanged, beforeText, afterText));
            }

            return;
        }

        CompareChildren(before, after, path, differences);
    }

    private static void CompareAttributes(XElement before, XElement after, string path, List<FormXmlDifference> differences)
    {
        var beforeAttributes = AttributesOf(before);
        var afterAttributes = AttributesOf(after);

        foreach (var (name, value) in beforeAttributes)
        {
            if (!afterAttributes.TryGetValue(name, out var afterValue))
            {
                differences.Add(new FormXmlDifference(Join(path, "@" + name), FormXmlDifferenceKind.AttributeRemoved, value, null));
            }
            else if (value != afterValue)
            {
                differences.Add(new FormXmlDifference(Join(path, "@" + name), FormXmlDifferenceKind.AttributeChanged, value, afterValue));
            }
        }

        foreach (var (name, value) in afterAttributes.Where(a => !beforeAttributes.ContainsKey(a.Key)))
        {
            differences.Add(new FormXmlDifference(Join(path, "@" + name), FormXmlDifferenceKind.AttributeAdded, null, value));
        }
    }

    private static void CompareChildren(XElement before, XElement after, string path, List<FormXmlDifference> differences)
    {
        var beforeChildren = SegmentsOf(before);
        var afterChildren = SegmentsOf(after);
        var afterBySegment = afterChildren.ToDictionary(c => c.Segment, c => c.Element);
        var beforeSegments = beforeChildren.Select(c => c.Segment).ToHashSet();

        foreach (var (segment, element) in beforeChildren)
        {
            if (afterBySegment.TryGetValue(segment, out var afterElement))
            {
                CompareElement(element, afterElement, Join(path, segment), differences);
            }
            else
            {
                differences.Add(new FormXmlDifference(Join(path, segment), FormXmlDifferenceKind.ElementRemoved, Show(element), null));
            }
        }

        foreach (var (segment, element) in afterChildren.Where(c => !beforeSegments.Contains(c.Segment)))
        {
            differences.Add(new FormXmlDifference(Join(path, segment), FormXmlDifferenceKind.ElementAdded, null, Show(element)));
        }

        // Keyed children (tabs, sections, controls, ...) are matched by key, so
        // reordering them would otherwise go unreported. Positional children
        // can't move without showing up as changed content above.
        var commonBefore = beforeChildren.Select(c => c.Segment).Where(afterBySegment.ContainsKey).ToList();
        var commonAfter = afterChildren.Select(c => c.Segment).Where(beforeSegments.Contains).ToList();
        for (var i = 0; i < commonBefore.Count; i++)
        {
            if (commonBefore[i] != commonAfter[i])
            {
                differences.Add(new FormXmlDifference(Join(path, commonBefore[i]), FormXmlDifferenceKind.ElementMoved,
                    (i + 1).ToString(), (commonAfter.IndexOf(commonBefore[i]) + 1).ToString()));
            }
        }
    }

    /// <summary>
    /// Each child's path segment: <c>name[key=value]</c> for an element with a
    /// stable identity, otherwise <c>name[position]</c>. A key that's missing
    /// or repeats among its siblings falls back to position, so every segment
    /// is unique within its parent.
    /// </summary>
    private static List<(string Segment, XElement Element)> SegmentsOf(XElement parent)
    {
        var segments = new List<(string, XElement)>();
        var used = new HashSet<string>();
        var positions = new Dictionary<string, int>();

        foreach (var child in parent.Elements())
        {
            var name = child.Name.LocalName;
            var position = positions[name] = positions.GetValueOrDefault(name) + 1;

            var segment = KeyOf(child) is { } key ? $"{name}[{key}]" : null;
            if (segment is null || !used.Add(segment))
            {
                segment = $"{name}[{position}]";
                used.Add(segment);
            }

            segments.Add((segment, child));
        }

        return segments;
    }

    private static string? KeyOf(XElement element) => element.Name.LocalName switch
    {
        "tab" or "section" or "Library" => Key(element, "name"),
        "control" => Key(element, "id"),
        "Handler" => Key(element, "functionName"),
        "data" => Key(element, "id"),
        "controlDescription" => Key(element, "forControl"),
        "event" => Key(element, "name") is { } name && Key(element, "attribute") is { } attribute ? $"{name},{attribute}" : Key(element, "name"),
        _ => null,
    };

    private static string? Key(XElement element, string attribute) =>
        element.Attribute(attribute)?.Value is { Length: > 0 } value ? $"{attribute}={value}" : null;

    private static Dictionary<string, string> AttributesOf(XElement element) =>
        element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(a => a.Name.LocalName, a => a.Value);

    private static string TextOf(XElement element) => string.IsNullOrWhiteSpace(element.Value) ? "" : element.Value;

    private static string Join(string path, string segment) => path.Length == 0 ? segment : $"{path}/{segment}";

    private static string Show(XElement element) => element.ToString(SaveOptions.DisableFormatting);
}

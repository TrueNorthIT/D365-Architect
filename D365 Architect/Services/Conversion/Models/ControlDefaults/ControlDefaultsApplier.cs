using System.Xml.Linq;

namespace D365Architect.Services.Conversion.Models.ControlDefaults;

/// <summary>
/// The logic for <see cref="ControlDefaultsSpec"/>, kept separate from every
/// spec's data: <see cref="Strip"/> is the export direction (drop what equals
/// its default), <see cref="Restore"/> the build direction (put it back, in
/// Dataverse's order). They must stay exact inverses — restoring a stripped
/// <c>parameters</c> yields the original FormXML — which each spec's tests
/// assert against a verbatim real sample.
/// </summary>
internal static class ControlDefaultsApplier
{
    /// <summary>Removes every parameter equal to its default from the reader's converted <c>parameters</c>; null when nothing is left.</summary>
    public static object? Strip(ControlDefaultsSpec spec, object? parameters)
    {
        if (parameters is not IDictionary<string, object> map)
        {
            return parameters;
        }

        var result = new Dictionary<string, object>(map);

        foreach (var parameter in spec.Parameters)
        {
            if (result.TryGetValue(parameter.Name, out var current) && IsDefault(parameter, current))
            {
                result.Remove(parameter.Name);
            }
        }

        foreach (var block in spec.Blocks)
        {
            if (result.TryGetValue(block.Name, out var current) && current is IDictionary<string, object> children)
            {
                var kept = new Dictionary<string, object>(children);
                foreach (var child in block.Defaults)
                {
                    if (kept.TryGetValue(child.Name, out var childValue) && IsDefault(child, childValue))
                    {
                        kept.Remove(child.Name);
                    }
                }

                result[block.Name] = kept;
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>Adds every omitted default back into the written `parameters` element, in Dataverse's order.</summary>
    public static void Restore(ControlDefaultsSpec spec, XElement parameters)
    {
        foreach (var block in spec.Blocks)
        {
            if (parameters.Element(block.Name) is not { } blockElement)
            {
                continue; // Nothing to complete — the block itself isn't there.
            }

            AddMissing(blockElement, block.Defaults);
            Reorder(blockElement, block.ChildOrder);
        }

        AddMissing(parameters, spec.Parameters);
        Reorder(parameters, spec.ParameterOrder);
    }

    private static bool IsDefault(ParameterDefault parameter, object current)
    {
        if (parameter.Attributes is not { Count: > 0 } expected)
        {
            return current is string text && text == parameter.Value;
        }

        return current is IDictionary<string, object> { Count: 2 } map
            && map.TryGetValue("value", out var value) && value is string valueText && valueText == parameter.Value
            && map.TryGetValue("attributes", out var attributes) && attributes is IDictionary<string, object> actual
            && actual.Count == expected.Count
            && expected.All(pair => actual.TryGetValue(pair.Key, out var actualValue) && actualValue is string s && s == pair.Value);
    }

    private static void AddMissing(XElement element, IEnumerable<ParameterDefault> defaults)
    {
        foreach (var parameter in defaults)
        {
            if (element.Element(parameter.Name) is not null)
            {
                continue;
            }

            var added = new XElement(parameter.Name, parameter.Value);
            foreach (var (name, value) in parameter.Attributes ?? new Dictionary<string, string>())
            {
                added.SetAttributeValue(name, value);
            }

            element.Add(added);
        }
    }

    /// <summary>Known names first in the given order, anything else after, in its original order.</summary>
    private static void Reorder(XElement element, IReadOnlyList<string> order)
    {
        if (order.Count == 0)
        {
            return;
        }

        var ranked = element.Elements()
            .Select((child, index) => (child, index, rank: Rank(order, child.Name.LocalName)))
            .OrderBy(x => x.rank).ThenBy(x => x.index)
            .Select(x => x.child)
            .ToList();

        element.RemoveNodes();
        element.Add(ranked);
    }

    private static int Rank(IReadOnlyList<string> order, string name)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == name)
            {
                return i;
            }
        }

        return order.Count;
    }
}

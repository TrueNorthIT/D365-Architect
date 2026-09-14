using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The core local-vs-existing option-value diff algorithm shared by
/// <see cref="Dataverse.AttributeMetadataJsonBuilder.BuildOptionChangePlans"/>
/// (a table column's local Picklist/MultiSelectPicklist options) and
/// <c>GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans</c> (a global
/// choice's own options) — identical matching/insert/rename/reorder logic
/// either way, only the resulting <see cref="OptionChangePlan"/>'s request
/// body shape differs (attribute-scoped vs. choice-scoped), which is why the
/// three builder delegates are supplied by the caller rather than this class
/// building bodies itself.
///
/// Match by <see cref="AttributeOptionDefinition.Value"/>: unmatched local
/// values insert, matched values with a different label rename, and — only
/// when the value-set is otherwise identical and just the order differs —
/// one reorder. An existing value missing from the local YAML is never
/// deleted automatically, mirroring
/// <see cref="AttributeImportAction.WouldRemove"/>'s same policy for a whole
/// column.
/// </summary>
internal static class OptionSetDiffer
{
    public static IReadOnlyList<OptionChangePlan> Diff(
        IReadOnlyList<AttributeOptionDefinition> localOptions,
        IReadOnlyList<AttributeOptionDefinition>? existingOptions,
        Func<AttributeOptionDefinition, OptionChangePlan> buildInsert,
        Func<int, string, OptionChangePlan> buildUpdate,
        Func<IReadOnlyList<int>, OptionChangePlan> buildOrder)
    {
        var plans = new List<OptionChangePlan>();
        var existingByValue = (existingOptions ?? []).ToDictionary(o => o.Value);

        foreach (var option in localOptions)
        {
            if (!existingByValue.TryGetValue(option.Value, out var existingOption))
            {
                plans.Add(buildInsert(option));
            }
            else if (existingOption.Label != option.Label)
            {
                plans.Add(buildUpdate(option.Value, option.Label));
            }
        }

        if (plans.Count == 0)
        {
            var localValues = localOptions.Select(o => o.Value).ToList();
            var existingValues = (existingOptions ?? []).Select(o => o.Value).ToList();
            if (localValues.Count == existingValues.Count && !localValues.SequenceEqual(existingValues) && localValues.ToHashSet().SetEquals(existingValues))
            {
                plans.Add(buildOrder(localValues));
            }
        }

        return plans;
    }
}

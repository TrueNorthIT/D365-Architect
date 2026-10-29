namespace D365Architect.Services.Conversion;

/// <summary>A <c>--table</c> name given to <c>solution export</c>/<c>solution import</c> matched no table.</summary>
public sealed class UnknownTableException(IReadOnlyList<string> unmatched, string where)
    : Exception($"No table named {string.Join(", ", unmatched.Select(t => $"'{t}'"))} found {where}.")
{
    public IReadOnlyList<string> Unmatched { get; } = unmatched;
}

/// <summary>
/// Applies the repeatable <c>--table</c> option shared by <c>solution
/// export</c> and <c>solution import</c>: matches the requested logical
/// names against the available tables (or table folder names) ignoring
/// case, and fails loudly on any name that matches nothing rather than
/// quietly doing less than was asked.
/// </summary>
public static class SolutionTableFilter
{
    /// <summary>Returns the subset of <paramref name="available"/> matching <paramref name="requested"/> (case-insensitive, original order and spelling kept). Throws <see cref="UnknownTableException"/> if any requested name matches none.</summary>
    /// <param name="available">All table names (or folder names) to choose from.</param>
    /// <param name="requested">The names given to <c>--table</c>.</param>
    /// <param name="where">Completes the error message, e.g. "in solution 'x'".</param>
    public static IReadOnlyList<string> Select(IEnumerable<string> available, IReadOnlyCollection<string> requested, string where)
    {
        var availableList = available.ToList();
        var unmatched = requested
            .Where(r => !availableList.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unmatched.Count > 0)
        {
            throw new UnknownTableException(unmatched, where);
        }

        return availableList.Where(a => requested.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
    }
}

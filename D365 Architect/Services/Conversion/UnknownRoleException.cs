namespace D365Architect.Services.Conversion;

/// <summary>A <c>--role</c> name given to <c>security export</c>/<c>security import</c> matched no role of the solution.</summary>
public sealed class UnknownRoleException(IReadOnlyList<string> unmatched, string solutionUniqueName)
    : Exception($"Role {string.Join(", ", unmatched.Select(r => $"'{r}'"))} is not in solution '{solutionUniqueName}'.")
{
    public IReadOnlyList<string> Unmatched { get; } = unmatched;

    public string SolutionUniqueName { get; } = solutionUniqueName;
}

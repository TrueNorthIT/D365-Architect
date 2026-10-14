namespace D365Architect.Services.Dataverse;

/// <summary>Thrown when a solution unique name doesn't match any solution in the environment.</summary>
public sealed class SolutionNotFoundException(string solutionUniqueName)
    : Exception($"No solution named '{solutionUniqueName}' was found in this environment.")
{
    public string SolutionUniqueName { get; } = solutionUniqueName;
}

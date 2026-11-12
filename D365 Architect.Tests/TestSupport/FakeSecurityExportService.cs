using D365Architect.Services.Conversion;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// A hand-written <see cref="ISecurityExportService"/> for tests of the services and commands that compose it
/// (<c>SolutionExportService</c>): returns <see cref="Result"/> (nothing by default) and records what it was asked for.
/// </summary>
public sealed class FakeSecurityExportService : ISecurityExportService
{
    /// <summary>What every call returns. Empty by default: no table has a security file and no role has a file.</summary>
    public SecurityExportResult Result { get; set; } = new([], []);

    /// <summary>Every call: (solution, tables, roles) as the caller passed them.</summary>
    public List<(string Solution, IReadOnlyCollection<string>? Tables, IReadOnlyCollection<string>? Roles)> Calls { get; } = [];

    public Task<SecurityExportResult> ExportSecurityAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, IReadOnlyCollection<string>? tables, IReadOnlyCollection<string>? roles, CancellationToken cancellationToken)
    {
        Calls.Add((solutionUniqueName, tables, roles));
        return Task.FromResult(Result);
    }
}

using Xunit;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// A <see cref="FactAttribute"/> for a live test (one that talks to a real Dataverse environment): skipped, and
/// reported as skipped rather than passed, unless both <c>D365ARCHITECT_LIVE_ENV</c> and <c>D365ARCHITECT_LIVE_PREFIX</c>
/// are set - so a plain <c>dotnet test</c> (CI, or a developer who hasn't opted in) never touches an environment.
/// Pair it with <c>[Trait("Category", "Live")]</c> so <c>--filter Category=Live</c> selects them. See
/// <see cref="LiveDataverse"/> and the "Live tests" section of docs/DEVELOPING.md.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (LiveDataverse.Settings is null)
        {
            Skip = $"Live test: set {LiveDataverse.EnvironmentVariable} and {LiveDataverse.PrefixVariable} to run it (see docs/DEVELOPING.md).";
        }
    }
}

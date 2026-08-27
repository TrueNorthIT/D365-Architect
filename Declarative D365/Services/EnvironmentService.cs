namespace Declarative_D365.Services;

/// <summary>
/// Placeholder in-memory implementation. Swap this out for real
/// Dataverse/D365 environment lookups and sync logic.
/// </summary>
public sealed class EnvironmentService : IEnvironmentService
{
    private static readonly string[] KnownEnvironments = ["dev", "test", "prod"];

    public IReadOnlyList<string> ListEnvironments() => KnownEnvironments;

    public async Task SyncAsync(string environmentName, CancellationToken cancellationToken)
    {
        if (!KnownEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown environment '{environmentName}'.", nameof(environmentName));
        }

        // Simulate work; replace with a real sync call.
        await Task.Delay(200, cancellationToken);
    }
}

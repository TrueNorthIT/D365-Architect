namespace D365Architect.Services;

/// <summary>
/// Example service shared by the "environment" command branch, showing how
/// sibling sub-commands can depend on the same injected service.
/// </summary>
public interface IEnvironmentService
{
    IReadOnlyList<string> ListEnvironments();

    Task SyncAsync(string environmentName, CancellationToken cancellationToken);
}

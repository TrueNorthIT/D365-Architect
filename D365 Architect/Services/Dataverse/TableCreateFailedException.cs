namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when Dataverse rejects (or fails) the create of a table, so the
/// message always names which table it was - what <c>solution import</c>
/// reports when it stops before its later stages.
/// </summary>
public sealed class TableCreateFailedException(string entityLogicalName, Exception inner)
    : Exception($"Creating table '{entityLogicalName}' failed: {inner.Message}", inner)
{
    public string EntityLogicalName { get; } = entityLogicalName;
}

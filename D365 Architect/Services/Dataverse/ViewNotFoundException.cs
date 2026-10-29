namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when a `*.view.yml` file's <c>savedQueryId</c> resolves to no live
/// view. A stale id is an error, never a fallback to the name or a create
/// (as for forms; see <see cref="FormNotFoundException"/>): it most likely
/// means the view was deleted, and silently creating a second one would hide
/// that. A file with no <c>savedQueryId</c> that matches no view by table +
/// name is created instead (REQ-23), so it never gets here.
/// </summary>
public sealed class ViewNotFoundException(string entityLogicalName, string viewName, Guid savedQueryId)
    : Exception($"No view with id '{savedQueryId}' was found (this YAML's own record of '{viewName}' on '{entityLogicalName}'). It may have been deleted since this file was last exported — re-export to get its current id, restore the deleted view, or remove 'savedQueryId' from the file to create it as a new view.")
{
    public string EntityLogicalName { get; } = entityLogicalName;
    public string ViewName { get; } = viewName;
    public Guid? SavedQueryId { get; } = savedQueryId;
}

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when <c>view import</c> can't find the live view a `*.view.yml`
/// file points at: by table + name when the file has no <c>savedQueryId</c>,
/// or by that id when it does (never falling back to the name - a stale id
/// is an error, as for forms; see <see cref="FormNotFoundException"/>).
/// Creating a brand-new view isn't supported yet - import only updates one
/// that already exists.
/// </summary>
public sealed class ViewNotFoundException : Exception
{
    public ViewNotFoundException(string entityLogicalName, string viewName)
        : base($"No view named '{viewName}' was found on '{entityLogicalName}'. Creating a new view isn't supported yet — view import only updates one that already exists in Dataverse.")
    {
        EntityLogicalName = entityLogicalName;
        ViewName = viewName;
    }

    public ViewNotFoundException(string entityLogicalName, string viewName, Guid savedQueryId)
        : base($"No view with id '{savedQueryId}' was found (this YAML's own record of '{viewName}' on '{entityLogicalName}'). It may have been deleted since this file was last exported — re-export to get its current id, or restore the deleted view first. Creating a new view isn't supported yet.")
    {
        EntityLogicalName = entityLogicalName;
        ViewName = viewName;
        SavedQueryId = savedQueryId;
    }

    public string EntityLogicalName { get; }
    public string ViewName { get; }
    public Guid? SavedQueryId { get; }
}

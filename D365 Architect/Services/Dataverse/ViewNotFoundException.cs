namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when <c>view import</c> can't find a live view matching a
/// `*.view.yml` file's table + name (the only identity it carries — see
/// <see cref="Conversion.Models.ViewDefinition"/>'s own doc comment).
/// Creating a brand-new view isn't supported yet — import only updates one
/// that already exists.
/// </summary>
public sealed class ViewNotFoundException(string entityLogicalName, string viewName)
    : Exception($"No view named '{viewName}' was found on '{entityLogicalName}'. Creating a new view isn't supported yet — view import only updates one that already exists in Dataverse.")
{
    public string EntityLogicalName { get; } = entityLogicalName;
    public string ViewName { get; } = viewName;
}

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when a table + display name (the only identity a `*.view.yml`
/// file carries — see <see cref="Conversion.Models.ViewDefinition"/>'s own
/// doc comment) matches more than one live <c>savedquery</c>. This tool
/// won't guess which one <c>view import</c> should update.
/// </summary>
public sealed class AmbiguousSavedQueryException(string entityLogicalName, string viewName, int matchCount)
    : Exception($"{matchCount} views named '{viewName}' were found on '{entityLogicalName}' — can't tell which one to update.")
{
    public string EntityLogicalName { get; } = entityLogicalName;
    public string ViewName { get; } = viewName;
    public int MatchCount { get; } = matchCount;
}

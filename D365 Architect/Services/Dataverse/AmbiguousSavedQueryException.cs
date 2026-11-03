namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when a table + display name matches more than one live
/// <c>savedquery</c> and the <c>*.view.yml</c> file carries no
/// <c>savedQueryId</c> to tell them apart (see
/// <see cref="Conversion.Models.ViewDefinition"/>). This tool won't guess
/// which one <c>view import</c> should update; the message lists the
/// candidate ids so the user can add the right one.
/// </summary>
public sealed class AmbiguousSavedQueryException(string entityLogicalName, string viewName, IReadOnlyList<Guid> candidateIds)
    : Exception($"{candidateIds.Count} views named '{viewName}' were found on '{entityLogicalName}' — can't tell which one to update. Add a `savedQueryId` to the view YAML to choose one of: {string.Join(", ", candidateIds)}.")
{
    public string EntityLogicalName { get; } = entityLogicalName;
    public string ViewName { get; } = viewName;
    public IReadOnlyList<Guid> CandidateIds { get; } = candidateIds;
    public int MatchCount => CandidateIds.Count;
}

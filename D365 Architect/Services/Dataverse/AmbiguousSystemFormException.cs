namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when a table + display name (the only identity a `*.form.yml`
/// file carries — see <see cref="Conversion.Models.FormDefinition"/>'s own
/// doc comment) matches more than one live <c>systemform</c>. This tool
/// won't guess which one <c>form build-xml</c> should patch.
/// </summary>
public sealed class AmbiguousSystemFormException(string entityLogicalName, string formName, int matchCount)
    : Exception($"{matchCount} forms named '{formName}' were found on '{entityLogicalName}' — can't tell which one to patch.")
{
    public string EntityLogicalName { get; } = entityLogicalName;
    public string FormName { get; } = formName;
    public int MatchCount { get; } = matchCount;

    /// <summary>
    /// The name + type form of the refusal (REQ-31): lists the candidate ids so the user can add the right
    /// <c>formId</c>. Test-first stub: the developer writes the message and <see cref="CandidateIds"/>.
    /// </summary>
    public AmbiguousSystemFormException(string entityLogicalName, string formName, IReadOnlyList<Guid> candidateIds)
        : this(entityLogicalName, formName, candidateIds.Count)
    {
    }

    /// <summary>The ids of the forms that matched; empty when thrown by the count-only constructor.</summary>
    public IReadOnlyList<Guid> CandidateIds { get; } = [];
}

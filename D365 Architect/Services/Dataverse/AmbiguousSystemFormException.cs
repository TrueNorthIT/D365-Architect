namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when a table + display name (+ form type, for <c>form import</c>)
/// matches more than one live <c>systemform</c>. This tool won't guess which
/// one to patch.
/// </summary>
public sealed class AmbiguousSystemFormException : Exception
{
    /// <summary>The name-only form, from <c>form build-xml</c>'s lookup, which knows only how many matched.</summary>
    public AmbiguousSystemFormException(string entityLogicalName, string formName, int matchCount)
        : base($"{matchCount} forms named '{formName}' were found on '{entityLogicalName}' — can't tell which one to patch.")
    {
        EntityLogicalName = entityLogicalName;
        FormName = formName;
        MatchCount = matchCount;
    }

    /// <summary>
    /// The name + type form of the refusal: lists the candidate ids so
    /// the user can add the right <c>formId</c> to the file.
    /// </summary>
    public AmbiguousSystemFormException(string entityLogicalName, string formName, IReadOnlyList<Guid> candidateIds)
        : base($"{candidateIds.Count} forms named '{formName}' of the same type were found on '{entityLogicalName}' — can't tell which one to update. Add the right one's id to the file as 'formId': {string.Join(", ", candidateIds)}.")
    {
        EntityLogicalName = entityLogicalName;
        FormName = formName;
        MatchCount = candidateIds.Count;
        CandidateIds = candidateIds;
    }

    public string EntityLogicalName { get; }
    public string FormName { get; }
    public int MatchCount { get; }

    /// <summary>The ids of the forms that matched; empty when thrown by the count-only constructor.</summary>
    public IReadOnlyList<Guid> CandidateIds { get; } = [];
}

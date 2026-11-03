namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when a <c>*.table.yml</c> can't be used to create a table: a
/// required value is missing, or the table is of a kind <c>table create</c>
/// doesn't support yet. Carries every problem found, not just the first, so
/// one run tells the user everything to fix. See <see cref="TableCreateValidator"/>.
/// </summary>
public sealed class TableCreateValidationException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

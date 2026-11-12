using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;

namespace D365Architect.Commands.Security;

/// <summary>
/// The cross-file step of a run that imports several security files (<c>security import</c> of a folder, <c>solution import</c>): after
/// every file is previewed and before anything is confirmed, entries covering the same role and privilege (by id, so a privilege several
/// tables share counts once) must agree. Each conflict is reported, and every file involved fails as a whole, so nothing is half-applied.
/// </summary>
internal static class SecurityConflictCheck
{
    public static void Apply(SecurityImportBatch batch, SolutionImportFlow flow)
    {
        foreach (var conflict in batch.FindConflicts())
        {
            ErrorConsole.Print($"{conflict.Message} Failing: {string.Join(", ", conflict.Sources)}.");
            foreach (var source in conflict.Sources)
            {
                flow.MarkFailed(source);
            }
        }
    }
}

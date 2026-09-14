namespace D365Architect.Services.Conversion;

/// <summary>
/// Exports every global choice in the environment (or, scoped to one
/// solution, just the ones it customizes) from Dataverse and converts them
/// into this tool's curated YAML — one file, a list of choices, not one
/// file per choice. A global choice is a small, low-cardinality-per-file
/// record (a name, a couple of labels, a handful of options); an
/// environment-wide inventory of them is much more useful as a single
/// scannable/diffable file than dozens of near-empty ones, unlike a table
/// (naturally one file each — a table's own columns are already the "list"
/// inside it) or a form/view (tied to one specific table + name).
/// </summary>
public interface IGlobalChoiceExportService
{
    /// <param name="environmentUrl">The D365 environment to read from.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="solutionUniqueName">
    /// When given, scopes the export to just the global choices that
    /// solution actually customizes (its Option Set solution components),
    /// instead of every global choice in the environment.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidDataException">Dataverse returned metadata this tool doesn't understand yet.</exception>
    /// <exception cref="Dataverse.SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution in the environment.</exception>
    Task<string> ExportGlobalChoicesAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, CancellationToken cancellationToken);
}

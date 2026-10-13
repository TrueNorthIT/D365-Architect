namespace D365Architect.Services.Conversion;

/// <summary>
/// Exports every asset type this tool supports (tables, views, forms, and
/// global choices) for a single solution, composing
/// <see cref="ITableExportService"/>/<see cref="IViewExportService"/>/
/// <see cref="IFormExportService"/>/<see cref="IGlobalChoiceExportService"/>
/// rather than reading from Dataverse directly — this service's only job is
/// discovering *which tables* the solution touches (via
/// <see cref="Dataverse.IDataverseClient.TryGetSolutionEntityLogicalNamesAsync"/>,
/// the one piece none of those four already do on their own) and fanning the
/// existing per-table/environment-wide export calls out across them. File
/// writing is left to the calling command, same as every other export
/// service here — this only returns curated YAML/lists in memory.
/// </summary>
public interface ISolutionExportService
{
    /// <param name="environmentUrl">The D365 environment to read from.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="solutionUniqueName">Unique name of the solution to export.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidDataException">Dataverse returned metadata this tool doesn't understand yet.</exception>
    /// <exception cref="Dataverse.SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution in the environment.</exception>
    Task<SolutionExportResult> ExportSolutionAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken);
}

/// <param name="EntityLogicalName">The table's logical name — also the sub-folder it's exported under.</param>
/// <param name="TableYaml">The table's own curated YAML, as <see cref="ITableExportService.ExportTableAsync"/> would write to <c>&lt;entity&gt;.table.yml</c>.</param>
/// <param name="Views">Every view this solution customizes on the table — empty when it customizes none.</param>
/// <param name="Forms">Every form this solution customizes on the table — empty when it customizes none.</param>
public sealed record SolutionExportEntity(string EntityLogicalName, string TableYaml, IReadOnlyList<ExportedView> Views, IReadOnlyList<ExportedForm> Forms);

/// <param name="Entities">One entry per table the solution's Entity components name — see <see cref="Dataverse.IDataverseClient.TryGetSolutionEntityLogicalNamesAsync"/>. Empty when the solution carries no table components at all (e.g. a choices-only solution).</param>
/// <param name="ChoicesYaml">The solution's global choices, as <see cref="IGlobalChoiceExportService.ExportGlobalChoicesAsync"/> would write to <c>choices.yml</c> at the solution's root — null when the solution customizes none, so the caller writes no file rather than an empty list.</param>
public sealed record SolutionExportResult(IReadOnlyList<SolutionExportEntity> Entities, string? ChoicesYaml);

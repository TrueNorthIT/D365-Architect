namespace D365Architect.Services.Conversion;

/// <summary>
/// Exports every view live from Dataverse for a single table and converts
/// each into this tool's curated YAML — the <see cref="ITableExportService"/>
/// counterpart for views. Unlike a table (one asset in, one YAML out), a
/// table can carry many views, so this returns one exported result per view
/// rather than a single YAML string.
/// </summary>
public interface IViewExportService
{
    /// <param name="environmentUrl">The D365 environment to read from.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="entityLogicalName">Logical name of the table whose views to export, e.g. "account".</param>
    /// <param name="solutionUniqueName">
    /// When given, scopes the export to just the views that solution
    /// actually customizes (its View solution components), instead of every
    /// view defined on the table.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidDataException">Dataverse returned metadata this tool doesn't understand yet.</exception>
    /// <exception cref="Dataverse.SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution in the environment.</exception>
    Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken);
}

/// <summary>
/// One exported view: its curated YAML, plus the filesystem-safe stem to
/// write it under (e.g. "active-accounts" for "active-accounts.view.yml") —
/// derived from the view's display name, since that's a view's only
/// practical identity (see <see cref="Models.ViewDefinition"/>).
/// </summary>
public sealed record ExportedView(string FileNameStem, string Yaml);

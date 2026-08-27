namespace D365Architect.Services.Conversion;

/// <summary>
/// Exports a single table's live definition from Dataverse and converts it
/// into this tool's curated YAML — the JSON-strategy counterpart to
/// <see cref="IXmlToYamlConverterService"/>'s file-based, XML-strategy flow.
/// </summary>
public interface ITableExportService
{
    /// <param name="environmentUrl">The D365 environment to read from.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="entityLogicalName">Logical name of the table to export, e.g. "account".</param>
    /// <param name="solutionUniqueName">
    /// When given, scopes the export to just the columns that solution
    /// actually customizes (its Attribute solution components), instead of
    /// the table's full merged metadata. Entity-level fields (display name,
    /// ownership, ...) still reflect the table as a whole — only the
    /// attribute list narrows.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidDataException">Dataverse returned metadata this tool doesn't understand yet.</exception>
    /// <exception cref="Dataverse.SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution in the environment.</exception>
    Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken);
}

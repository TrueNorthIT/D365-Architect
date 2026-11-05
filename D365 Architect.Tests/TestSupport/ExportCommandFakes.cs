using D365Architect.Services.Conversion;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Fakes for the five export services, used by the export-command error
/// handling tests. Each one throws <paramref name="exportFailure"/> — the
/// unanticipated exception that has to land in the command's final
/// catch-all instead of escaping as an unhandled crash (the same "fail
/// loudly" convention as <see cref="FakeDataverseClient"/>).
/// </summary>
public sealed class GlobalChoiceExportFake(Exception exportFailure) : IGlobalChoiceExportService
{
    public Task<string> ExportGlobalChoicesAsync(Uri environmentUrl, string accessToken, string? solutionUniqueName, CancellationToken cancellationToken) => throw exportFailure;
}

public sealed class TableExportFake(Exception exportFailure) : ITableExportService
{
    public Task<string> ExportTableAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) => throw exportFailure;
}

public sealed class ViewExportFake(Exception exportFailure) : IViewExportService
{
    public Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) => throw exportFailure;
}

public sealed class FormExportFake(Exception exportFailure) : IFormExportService
{
    public Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken) => throw exportFailure;

    public Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken) => throw exportFailure;

    public ExportedForm ExportFormFromXml(string path, string entityLogicalName, string? type) => throw exportFailure;
}

public sealed class SolutionExportFake(Exception exportFailure) : ISolutionExportService
{
    public Task<SolutionExportResult> ExportSolutionAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken, IReadOnlyCollection<string>? tables = null) => throw exportFailure;
}

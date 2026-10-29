using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Creates a table (and only its primary name column) from a curated
/// <see cref="EntityDefinition"/> when it doesn't exist live yet - the
/// counterpart <c>table import</c> deliberately never provides. Everything
/// else in the file is left for <c>table import</c>. Added by the test-first
/// pass; behaviour pinned by <c>TableCreateServiceTests</c>.
///
/// <see cref="IImportService{TInput,TPreview}.PreviewAsync"/> checks existence first: a table that
/// already exists previews as <see cref="TableCreatePreview.AlreadyExists"/> and is never validated,
/// so older exports without <c>primaryNameAttribute</c> don't break <c>solution import</c>. Otherwise
/// it validates (throws <see cref="Dataverse.TableCreateValidationException"/>) and builds the body,
/// writing nothing. <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> is the same as the
/// overload below with no solution.
/// </summary>
public interface ITableCreateService : IImportService<EntityDefinition, TableCreatePreview>
{
    /// <param name="environmentUrl">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>.</param>
    /// <param name="accessToken">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>.</param>
    /// <param name="preview">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>. A preview that is <see cref="TableCreatePreview.AlreadyExists"/> writes nothing.</param>
    /// <param name="solutionUniqueName">When given, the new table joins this solution as it is created (<c>MSCRM.SolutionUniqueName</c>).</param>
    /// <param name="cancellationToken">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>.</param>
    /// <exception cref="Dataverse.TableCreateFailedException">Dataverse rejected the create; the message names the table.</exception>
    Task ApplyAsync(Uri environmentUrl, string accessToken, TableCreatePreview preview, string? solutionUniqueName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates every table in <paramref name="previews"/> that doesn't exist yet (<see cref="TableCreatePreview.AlreadyExists"/> ones are left out)
    /// in ONE transaction: a single <see cref="Dataverse.IDataverseClient.ExecuteTransactionAsync"/> call whose writes are
    /// <see cref="Dataverse.DataverseWrite.CreateEntity"/>, in the order given, each carrying <paramref name="solutionUniqueName"/>. If any
    /// create fails none is kept; the <see cref="Dataverse.TableCreateFailedException"/> names the table whose request Dataverse reported
    /// as failed (<see cref="Dataverse.DataverseTransactionFailedException.FailedContentId"/>, 1-based among the requests actually sent).
    /// </summary>
    Task ApplyAllAsync(Uri environmentUrl, string accessToken, IReadOnlyList<TableCreatePreview> previews, string? solutionUniqueName, CancellationToken cancellationToken);

    /// <summary>Publishes <paramref name="preview"/>'s table (<c>table create --publish</c>, REQ-37) — the same <see cref="Dataverse.IDataverseClient.PublishEntityAsync"/> call form import makes.</summary>
    Task PublishAsync(Uri environmentUrl, string accessToken, TableCreatePreview preview, CancellationToken cancellationToken);
}

/// <param name="EntityLogicalName">The table this preview is for.</param>
/// <param name="AlreadyExists">True when the table is already live: nothing to create, <paramref name="RequestBody"/> is null.</param>
/// <param name="RequestBody">The <c>EntityMetadata</c> body that would be POSTed; null when <paramref name="AlreadyExists"/>.</param>
/// <param name="PrimaryNameColumn">Logical name of the primary name column created with the table; null when <paramref name="AlreadyExists"/>.</param>
/// <param name="DeferredColumns">Logical names of the file's other columns, left for <c>table import</c>; empty when <paramref name="AlreadyExists"/>.</param>
public sealed record TableCreatePreview(string EntityLogicalName, bool AlreadyExists, JsonObject? RequestBody, string? PrimaryNameColumn, IReadOnlyList<string> DeferredColumns)
{
    /// <summary>True when there is a table to create.</summary>
    public bool HasChanges => !AlreadyExists;
}

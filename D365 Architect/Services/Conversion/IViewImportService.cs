using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Writes a curated <see cref="ViewDefinition"/>'s FetchXML/LayoutXML back
/// into Dataverse — directly from the YAML. Much simpler than
/// <see cref="IFormImportService"/>: a view's FetchXml/LayoutXml are kept
/// verbatim (see <see cref="ViewDefinition"/>'s own doc comment), not
/// decomposed and rebuilt through a writer, so there's no id-resynthesis to
/// cancel out — the diff compares the two sides directly.
///
/// Only ever updates a view that already exists (by table + name — the same
/// identity <c>view export</c> uses); creating a brand-new view isn't
/// supported yet (see <see cref="Dataverse.ViewNotFoundException"/>). Only
/// <c>Description</c>/<c>FetchXml</c>/<c>LayoutXml</c> are written —
/// <c>QueryType</c>/<c>IsDefault</c>/<c>IsQuickFindQuery</c> are documented
/// on <see cref="ViewDefinition"/> itself as fields applying a YAML file
/// back doesn't change.
///
/// <see cref="IImportService{TInput,TPreview}.PreviewAsync"/> throws
/// <see cref="Dataverse.ViewNotFoundException"/> when no view named
/// <c>view.Name</c> exists yet on <c>view.Entity</c>, and
/// <see cref="Dataverse.AmbiguousSavedQueryException"/> when more than one
/// view matches that table + name. <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>
/// doesn't publish the change.
/// </summary>
public interface IViewImportService : IImportService<ViewDefinition, ViewImportPreview>
{
    /// <summary>
    /// As <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>, adding whatever it creates or changes
    /// to <paramref name="solutionUniqueName"/> (null = no solution), and creating the view when
    /// <paramref name="preview"/> is <see cref="ViewImportPreview.IsCreate"/> (REQ-22, REQ-23).
    /// Test-first stub: the developer implements this; behaviour pinned by <c>ViewImportServiceCreateTests</c>.
    /// </summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, string? solutionUniqueName, CancellationToken cancellationToken);
}

/// <param name="SavedQueryId">The view's id — what <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> updates.</param>
/// <param name="ExistingDescription">The view's current live description.</param>
/// <param name="NewDescription">The local YAML's description — null means the YAML never had one, so it's left untouched, not cleared.</param>
/// <param name="ExistingFetchXml">The view's current live FetchXML.</param>
/// <param name="NewFetchXml">The local YAML's FetchXML.</param>
/// <param name="ExistingLayoutXml">The view's current live LayoutXML.</param>
/// <param name="NewLayoutXml">The local YAML's LayoutXML.</param>
public sealed record ViewImportPreview(
    Guid SavedQueryId,
    string? ExistingDescription,
    string? NewDescription,
    string? ExistingFetchXml,
    string? NewFetchXml,
    string? ExistingLayoutXml,
    string? NewLayoutXml)
{
    /// <summary>
    /// Set when the YAML's <c>savedQueryId</c> resolved to a live view with a
    /// different name (a legitimate rename is possible, so only a warning;
    /// the id stays authoritative). A different table is refused instead —
    /// see <see cref="Dataverse.ViewIdentityMismatchException"/>. Null for a
    /// by-name lookup or when the names agree.
    /// </summary>
    public string? IdentityMismatchWarning { get; init; }

    /// <summary>
    /// True when the YAML matched no existing view (no <c>savedQueryId</c>, and no view of that name on that
    /// table), so <c>ApplyAsync</c> creates one (REQ-23). Then <see cref="SavedQueryId"/> is <see cref="Guid.Empty"/>,
    /// the <c>Existing*</c> values are null and <see cref="HasChanges"/> is true.
    /// </summary>
    public bool IsCreate { get; init; }

    /// <summary>
    /// False when the local YAML's description/FetchXml/LayoutXml (whichever
    /// it actually has — a null one is never compared, since omitting a
    /// field means "don't touch this", not "clear it") all already match
    /// what's live.
    /// </summary>
    public bool HasChanges =>
        (NewDescription is not null && NewDescription != ExistingDescription)
        || (NewFetchXml is not null && NewFetchXml != ExistingFetchXml)
        || (NewLayoutXml is not null && NewLayoutXml != ExistingLayoutXml);
}

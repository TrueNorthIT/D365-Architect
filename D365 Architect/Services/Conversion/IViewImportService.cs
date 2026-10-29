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
/// Updates the view the YAML's <c>savedQueryId</c> names, or otherwise the
/// one matching its table + name (the same identity <c>view export</c>
/// uses). A hand-written file matching nothing by table + name is created
/// as a public view (<see cref="ViewImportPreview.IsCreate"/>, REQ-23). Only
/// <c>Description</c>/<c>FetchXml</c>/<c>LayoutXml</c> are written —
/// <c>QueryType</c>/<c>IsDefault</c>/<c>IsQuickFindQuery</c> are documented
/// on <see cref="ViewDefinition"/> itself as fields applying a YAML file
/// back doesn't change.
///
/// A file may give <see cref="ViewDefinition.Columns"/> instead of a raw
/// LayoutXML: the layout is then generated, and the FetchXML gains any
/// listed column it doesn't query (REQ-24 to REQ-26). The layout's
/// <c>object</c> is always the table's live type code (REQ-27).
///
/// <see cref="IImportService{TInput,TPreview}.PreviewAsync"/> throws
/// <see cref="Dataverse.ViewNotFoundException"/> when <c>savedQueryId</c>
/// matches nothing (never a create),
/// <see cref="Dataverse.AmbiguousSavedQueryException"/> when more than one
/// view matches that table + name, and <see cref="InvalidDataException"/>
/// for a file it can't act on (both <c>columns</c> and <c>layoutXml</c>, a
/// bad <c>columns</c> list, a missing table, or a new view lacking what a
/// create needs). <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>
/// doesn't publish the change.
/// </summary>
public interface IViewImportService : IImportService<ViewDefinition, ViewImportPreview>
{
    /// <summary>
    /// As <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>, adding whatever it creates or changes
    /// to <paramref name="solutionUniqueName"/> (null = no solution, sent as <c>MSCRM.SolutionUniqueName</c>),
    /// and creating the view when <paramref name="preview"/> is <see cref="ViewImportPreview.IsCreate"/>
    /// (REQ-22, REQ-23) — see <see cref="Dataverse.IDataverseClient.CreateSavedQueryAsync"/> for the body.
    /// </summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, string? solutionUniqueName, CancellationToken cancellationToken);

    /// <summary>Publishes the view's table, <see cref="ViewImportPreview.Entity"/> (what <c>view import</c> does once it has applied, unless <c>--no-publish</c>): Dataverse publishes a view as part of its table, so there's no narrower request.</summary>
    Task PublishAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, CancellationToken cancellationToken);
}

/// <param name="SavedQueryId">The view's id — what <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> updates (<see cref="Guid.Empty"/> for a create).</param>
/// <param name="ExistingDescription">The view's current live description.</param>
/// <param name="NewDescription">The local YAML's description — null means the YAML never had one, so it's left untouched, not cleared.</param>
/// <param name="ExistingFetchXml">The view's current live FetchXML, less the <c>savedqueryid</c> Dataverse stamps on its root when <paramref name="NewFetchXml"/> has none (so a hand-written file compares like with like).</param>
/// <param name="NewFetchXml">The FetchXML to write: the YAML's own, or — with <c>columns</c> — it (else the live one, else a minimal one) plus any listed column it lacked.</param>
/// <param name="ExistingLayoutXml">The view's current live LayoutXML.</param>
/// <param name="NewLayoutXml">The LayoutXML to write: the YAML's own (its <c>object</c> corrected to the live type code), or the one generated from its <c>columns</c>.</param>
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

    /// <summary>The YAML's view name — the create's <c>name</c>.</summary>
    public string Name { get; init; } = "";

    /// <summary>The YAML's table logical name — the create's <c>returnedtypecode</c>.</summary>
    public string Entity { get; init; } = "";

    /// <summary>
    /// False when the local YAML's description/FetchXml/LayoutXml (whichever
    /// it actually has — a null one is never compared, since omitting a
    /// field means "don't touch this", not "clear it") all already match
    /// what's live.
    /// </summary>
    public bool HasChanges => IsCreate || ChangeCount > 0;

    /// <summary>How many of the three fields differ (see <see cref="HasChanges"/>).</summary>
    public int ChangeCount =>
        (NewDescription is not null && NewDescription != ExistingDescription ? 1 : 0)
        + (NewFetchXml is not null && NewFetchXml != ExistingFetchXml ? 1 : 0)
        + (NewLayoutXml is not null && NewLayoutXml != ExistingLayoutXml ? 1 : 0);
}

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
/// </summary>
public interface IViewImportService
{
    /// <exception cref="Dataverse.ViewNotFoundException">No view named <c>view.Name</c> exists yet on <c>view.Entity</c>.</exception>
    /// <exception cref="Dataverse.AmbiguousSavedQueryException">More than one view matches that table + name.</exception>
    Task<ViewImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, ViewDefinition view, CancellationToken cancellationToken);

    /// <summary>Writes <paramref name="preview"/>'s changes back to the same view it was previewed against. Doesn't publish the change.</summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, CancellationToken cancellationToken);
}

/// <param name="SavedQueryId">The view's id — what <see cref="IViewImportService.ApplyAsync"/> updates.</param>
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

namespace D365Architect.Services.Conversion;

/// <summary>
/// The preview/apply shape every import service follows — look up the live
/// item and build a preview of what would change without writing anything,
/// then write exactly what was previewed. <see cref="IFormImportService"/>,
/// <see cref="ITableImportService"/>, and <see cref="IViewImportService"/>
/// each extend this rather than redeclaring it — see each one's own doc
/// comment for what its <typeparamref name="TInput"/>/
/// <typeparamref name="TPreview"/> actually are, and for anything beyond
/// this shared shape it needs on top (e.g. form import's separate publish
/// step — see <see cref="IFormImportService"/> — which doesn't fit here
/// since table/view import never publish at all).
/// </summary>
/// <typeparam name="TInput">The curated local definition being imported (e.g. <c>FormDefinition</c>).</typeparam>
/// <typeparam name="TPreview">What previewing the change produces (e.g. <c>FormImportPreview</c>).</typeparam>
public interface IImportService<TInput, TPreview>
{
    /// <summary>Looks up the live item and builds a preview of the change — without writing anything.</summary>
    Task<TPreview> PreviewAsync(Uri environmentUrl, string accessToken, TInput input, CancellationToken cancellationToken);

    /// <summary>Writes exactly what <paramref name="preview"/> already built, back to the same item it was previewed against. Nothing more — see the implementing interface's own doc comment for whatever else its import command does around this call.</summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, TPreview preview, CancellationToken cancellationToken);
}

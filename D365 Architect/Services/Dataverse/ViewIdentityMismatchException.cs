namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when a <c>*.view.yml</c>'s <c>savedQueryId</c> resolves to a live
/// view on a different table than the YAML's own <c>entity</c> — almost
/// certainly an id copied into the wrong file, so importing would overwrite
/// an unrelated view. A differing <em>name</em> alone is never an error (views
/// are legitimately renamed); see <see cref="Conversion.ViewImportPreview.IdentityMismatchWarning"/>.
/// </summary>
public sealed class ViewIdentityMismatchException(Guid savedQueryId, string yamlEntity, string yamlName, string liveEntity, string? liveName)
    : Exception($"The savedQueryId '{savedQueryId}' belongs to the view '{liveName}' on '{liveEntity}', but this file's own view is '{yamlName}' on '{yamlEntity}'. Refusing to import so an unrelated view isn't overwritten — fix the savedQueryId (or the entity) in the YAML.")
{
    public Guid SavedQueryId { get; } = savedQueryId;
    public string YamlEntity { get; } = yamlEntity;
    public string LiveEntity { get; } = liveEntity;
}

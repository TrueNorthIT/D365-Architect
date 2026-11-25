namespace D365Architect.Services.Conversion;

/// <summary>
/// Two or more <c>*.view.yml</c> files of one <c>solution import</c> carry
/// the same <c>savedQueryId</c>. <c>solution export</c> names a view's file
/// after the view and never removes the old one, so exporting again after a
/// rename leaves both; importing them would rename the view back. Any repeat
/// is refused, matching names included, since one file must be a stray copy.
/// </summary>
public sealed class DuplicateSavedQueryIdException(Guid savedQueryId, IReadOnlyList<string> paths)
    : Exception($"The savedQueryId '{savedQueryId}' is in {paths.Count} view files: {string.Join(", ", paths)}. One view can only come from one file, so none of them was imported. An older file is probably left over from before the view was renamed ('solution export' doesn't remove it): delete the one that's out of date.")
{
    public Guid SavedQueryId { get; } = savedQueryId;
    public IReadOnlyList<string> Paths { get; } = paths;
}

using D365Architect.Commands.Solution;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Commands.View;

/// <summary>
/// The cross-file step of <c>solution import</c> for views: after every file
/// is previewed and before anything is confirmed, no two view files of the
/// run may carry the same <c>savedQueryId</c>. Checked from the files
/// themselves, so an unchanged preview counts too: after a rename the new
/// file is unchanged and only the stale one would write. Every file involved
/// fails (see <see cref="DuplicateSavedQueryIdException"/>).
/// </summary>
internal static class SavedQueryIdConflictCheck
{
    public static void Apply(IReadOnlyList<string> viewPaths, SolutionImportFlow flow)
    {
        var duplicates = viewPaths
            .Select(path => (Path: path, Id: TryReadSavedQueryId(path)))
            .Where(view => view.Id is not null)
            .GroupBy(view => view.Id!.Value)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicates)
        {
            var paths = group.Select(view => view.Path).ToList();
            var conflict = new DuplicateSavedQueryIdException(group.Key, paths);
            foreach (var path in paths)
            {
                ErrorConsole.Print($"'{path}' failed: {conflict.Message}");
                flow.MarkFailed(path);
            }
        }
    }

    // A file that can't be read has already failed in its own step, which said why.
    private static Guid? TryReadSavedQueryId(string path)
    {
        try
        {
            ViewDefinition? view = ViewYamlDeserializer.FromYaml(File.ReadAllText(path));
            return view?.SavedQueryId;
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or IOException)
        {
            return null;
        }
    }
}

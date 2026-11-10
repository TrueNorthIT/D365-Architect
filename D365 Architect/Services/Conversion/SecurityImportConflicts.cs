using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <param name="RoleName">The role both entries are about.</param>
/// <param name="PrivilegeName">The privilege's platform name.</param>
/// <param name="Entries">Every entry covering this role and privilege: which file (source), where in it, and the level it asks for.</param>
public sealed record SecurityConflict(string RoleName, string PrivilegeName, IReadOnlyList<(string Source, string Entry, SecurityAccessLevel Desired)> Entries)
{
    /// <summary>Every file involved; each fails as a whole, so nothing is half-applied.</summary>
    public IReadOnlyList<string> Sources => Entries.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public string Message =>
        $"Conflict on role '{RoleName}', privilege '{PrivilegeName}': "
        + string.Join("; ", Entries.Select(e => $"{e.Source} ({e.Entry}) asks for {e.Desired}"))
        + ". One privilege can only hold one level; nothing was written for it.";
}

/// <summary>
/// Finds entries that cover the same role and privilege (by id: a privilege several tables share counts once) but ask for different levels,
/// across every preview given. Used inside one file by <see cref="SecurityImportService"/> and across every file of a run by
/// <see cref="SecurityImportBatch"/>.
/// </summary>
public static class SecurityImportConflicts
{
    public static IReadOnlyList<SecurityConflict> Find(IEnumerable<(string Source, SecurityImportPreview Preview)> previews) =>
        previews
            .SelectMany(p => p.Preview.Roles.SelectMany(r => r.Covered.Select(c => (p.Source, Role: r, Covered: c))))
            .GroupBy(e => (e.Role.RoleId, e.Covered.PrivilegeId))
            .Where(g => g.Select(e => e.Covered.Desired).Distinct().Count() > 1)
            .Select(g => new SecurityConflict(g.First().Role.RoleName, g.First().Covered.PrivilegeName,
                g.Select(e => (e.Source, e.Covered.Entry, e.Covered.Desired)).ToList()))
            .ToList();
}

/// <summary>
/// Every security preview of one run (one <c>security import</c> or <c>solution import</c>), so they can be checked against each other
/// before anything is confirmed (<see cref="FindConflicts"/>) and a privilege several files agree on is written once, by the first of them
/// to be applied (<see cref="Unwritten"/>/<see cref="MarkWritten"/>).
/// </summary>
public sealed class SecurityImportBatch
{
    private readonly List<(string Source, SecurityImportPreview Preview)> _previews = [];
    private readonly HashSet<(Guid RoleId, Guid PrivilegeId)> _written = [];

    /// <summary>Records a file's preview, changed or not: an unchanged file still states levels another file may disagree with.</summary>
    public void Add(string source, SecurityImportPreview preview)
    {
        lock (_previews)
        {
            _previews.Add((source, preview));
        }
    }

    public IReadOnlyList<SecurityConflict> FindConflicts()
    {
        lock (_previews)
        {
            return SecurityImportConflicts.Find(_previews);
        }
    }

    /// <summary><paramref name="preview"/> without the writes an earlier file of this run already made (the same privilege, at the same level: a different level would have been a conflict).</summary>
    public SecurityImportPreview Unwritten(SecurityImportPreview preview) => preview with
    {
        Roles = preview.Roles.Select(r => r with
        {
            Changes = r.Changes.Where(c => c.Action == PrivilegeChangeAction.KeptNotRemoved || c.PrivilegeId == Guid.Empty || !_written.Contains((r.RoleId, c.PrivilegeId))).ToList(),
        }).ToList(),
    };

    /// <summary>Notes the writes <paramref name="applied"/> made, so a later file agreeing with them doesn't write them again.</summary>
    public void MarkWritten(SecurityImportPreview applied)
    {
        foreach (var role in applied.Roles)
        {
            foreach (var change in role.Changes.Where(c => c.Action != PrivilegeChangeAction.KeptNotRemoved && c.PrivilegeId != Guid.Empty))
            {
                _written.Add((role.RoleId, change.PrivilegeId));
            }
        }
    }
}

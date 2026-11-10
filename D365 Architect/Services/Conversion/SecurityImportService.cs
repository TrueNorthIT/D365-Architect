using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class SecurityImportService(IDataverseClient dataverseClient) : ISecurityImportService
{
    public async Task<SecurityImportPreview> PreviewTableSecurityAsync(Uri environmentUrl, string accessToken, TableSecurityDefinition input, bool allowRemovals, CancellationToken cancellationToken)
    {
        // Every role is found before anything is read or planned, so one missing role fails the whole file.
        var roles = new List<(TableRoleSecurity Row, SecurityRole Role)>();
        foreach (var row in input.Roles)
        {
            roles.Add((row, await FindRoleAsync(environmentUrl, accessToken, row.Name, cancellationToken)));
        }

        var plans = new List<RoleSecurityPlan>();
        foreach (var (row, role) in roles)
        {
            var live = await dataverseClient.GetRolePrivilegesAsync(environmentUrl, accessToken, role.RoleId, cancellationToken);
            var liveLevels = SecurityPrivilegeLevels.OnTable(live, input.Table);

            var changes = new List<PrivilegeChange>();
            foreach (var right in Enum.GetValues<TablePrivilege>())
            {
                var from = liveLevels.TryGetValue(right, out var level) ? level : SecurityAccessLevel.None;
                AddChange(changes, PrivilegeIdentity.ForTable(input.Table, right), from, SecurityPrivilegeLevels.Get(row, right), allowRemovals);
            }

            plans.Add(new RoleSecurityPlan(row.Name, role.RoleId, changes));
        }

        return new SecurityImportPreview(input.Table, plans);
    }

    public async Task<SecurityImportPreview> PreviewRoleAsync(Uri environmentUrl, string accessToken, RoleDefinition input, string solutionUniqueName, bool allowRemovals, CancellationToken cancellationToken)
    {
        var role = await FindRoleAsync(environmentUrl, accessToken, input.Name, cancellationToken);
        var solutionTables = await dataverseClient.TryGetSolutionEntityLogicalNamesAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
            ?? throw new SolutionNotFoundException(solutionUniqueName);
        var inSolution = new HashSet<string>(solutionTables, StringComparer.OrdinalIgnoreCase);

        var live = await dataverseClient.GetRolePrivilegesAsync(environmentUrl, accessToken, role.RoleId, cancellationToken);
        var changes = new List<PrivilegeChange>();

        // Non-table privileges: the file is the full picture, so one held live but not listed is a removal candidate.
        var liveNamed = SecurityPrivilegeLevels.NonTable(live);
        var fileNamed = input.Privileges ?? new Dictionary<string, SecurityAccessLevel>();
        foreach (var (name, to) in fileNamed)
        {
            var from = liveNamed.TryGetValue(name, out var level) ? level : SecurityAccessLevel.None;
            AddChange(changes, PrivilegeIdentity.ForName(name), from, to, allowRemovals);
        }

        foreach (var (name, from) in liveNamed.Where(p => !fileNamed.Keys.Contains(p.Key, StringComparer.OrdinalIgnoreCase)).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddChange(changes, PrivilegeIdentity.ForName(name), from, SecurityAccessLevel.None, allowRemovals);
        }

        // Tables outside the solution: every one listed in the file or held live. The solution's own tables belong to their
        // security files, so they are never compared here, even when the file lists one.
        var fileTables = (input.Tables ?? new Dictionary<string, TablePrivilegeLevels>())
            .Where(t => !inSolution.Contains(t.Key))
            .ToList();
        var liveOnlyTables = live
            .Where(p => p.Privilege.IsTablePrivilege && !inSolution.Contains(p.Privilege.TableLogicalName!))
            .Select(p => p.Privilege.TableLogicalName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => !fileTables.Any(f => string.Equals(f.Key, t, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

        var tablesToCompare = fileTables.Select(t => (Table: t.Key, Levels: (TablePrivilegeLevels?)t.Value))
            .Concat(liveOnlyTables.Select(t => (Table: t, Levels: (TablePrivilegeLevels?)null)));
        foreach (var (table, levels) in tablesToCompare)
        {
            var liveLevels = SecurityPrivilegeLevels.OnTable(live, table);
            foreach (var right in Enum.GetValues<TablePrivilege>())
            {
                var from = liveLevels.TryGetValue(right, out var level) ? level : SecurityAccessLevel.None;
                var to = (levels is null ? null : SecurityPrivilegeLevels.Get(levels, right)) ?? SecurityAccessLevel.None;
                AddChange(changes, PrivilegeIdentity.ForTable(table, right), from, to, allowRemovals);
            }
        }

        return new SecurityImportPreview(input.Name, [new RoleSecurityPlan(input.Name, role.RoleId, changes)]);
    }

    public async Task ApplyAsync(Uri environmentUrl, string accessToken, SecurityImportPreview preview, CancellationToken cancellationToken)
    {
        foreach (var plan in preview.Roles)
        {
            var adds = plan.Changes
                .Where(c => c.Action == PrivilegeChangeAction.Set)
                .Select(c => new RolePrivilege(c.Privilege, SecurityAccessLevelMapping.ToDepth(c.To)
                    ?? throw new InvalidOperationException($"A set of {Describe(c.Privilege)} must have an access level other than None.")))
                .ToList();
            if (adds.Count > 0)
            {
                await dataverseClient.AddRolePrivilegesAsync(environmentUrl, accessToken, plan.RoleId, adds, cancellationToken);
            }

            var removals = plan.Changes
                .Where(c => c.Action == PrivilegeChangeAction.Remove)
                .Select(c => c.Privilege)
                .ToList();
            if (removals.Count > 0)
            {
                await dataverseClient.RemoveRolePrivilegesAsync(environmentUrl, accessToken, plan.RoleId, removals, cancellationToken);
            }
        }
    }

    /// <summary>How a privilege is named in the preview: its platform name, or "Right on table" for a table privilege.</summary>
    public static string Describe(PrivilegeIdentity privilege) =>
        privilege.IsTablePrivilege ? $"{privilege.TableRight} on {privilege.TableLogicalName}" : privilege.PrivilegeName ?? "(unnamed privilege)";

    private async Task<SecurityRole> FindRoleAsync(Uri environmentUrl, string accessToken, string roleName, CancellationToken cancellationToken) =>
        await dataverseClient.TryGetRoleByNameAsync(environmentUrl, accessToken, roleName, cancellationToken)
            ?? throw new RoleNotFoundException(roleName);

    private static void AddChange(List<PrivilegeChange> changes, PrivilegeIdentity privilege, SecurityAccessLevel from, SecurityAccessLevel to, bool allowRemovals)
    {
        if (from == to)
        {
            return;
        }

        var action = to != SecurityAccessLevel.None
            ? PrivilegeChangeAction.Set
            : allowRemovals ? PrivilegeChangeAction.Remove : PrivilegeChangeAction.KeptNotRemoved;
        changes.Add(new PrivilegeChange(privilege, from, to, action));
    }
}

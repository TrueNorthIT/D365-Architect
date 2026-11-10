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

        // Every privilege is resolved before anything is planned, so a right the table doesn't have fails the file, not its write.
        var rights = Enum.GetValues<TablePrivilege>();
        var resolved = await dataverseClient.ResolvePrivilegesAsync(environmentUrl, accessToken, rights.Select(r => PrivilegeIdentity.ForTable(input.Table, r)).ToList(), cancellationToken);

        var missing = new List<string>();
        var plans = new List<RoleSecurityPlan>();
        foreach (var (row, role) in roles)
        {
            var live = await dataverseClient.GetRolePrivilegesAsync(environmentUrl, accessToken, role.RoleId, cancellationToken);
            var liveLevels = SecurityPrivilegeLevels.OnTable(live, input.Table);

            var changes = new List<PrivilegeChange>();
            var covered = new List<CoveredPrivilege>();
            for (var i = 0; i < rights.Length; i++)
            {
                var right = rights[i];
                var desired = SecurityPrivilegeLevels.Get(row, right);
                if (resolved[i] is not { } privilege)
                {
                    // A right the table doesn't have (Assign on an organisation-owned table) can only be None.
                    if (desired != SecurityAccessLevel.None)
                    {
                        missing.Add($"{right} on {input.Table}");
                    }

                    continue;
                }

                covered.Add(new CoveredPrivilege(privilege.PrivilegeId, privilege.Name, $"{right} on {input.Table}", desired));
                var from = liveLevels.TryGetValue(right, out var level) ? level : SecurityAccessLevel.None;
                AddChange(changes, PrivilegeIdentity.ForTable(input.Table, right), privilege, input.Table, from, desired, allowRemovals);
            }

            plans.Add(new RoleSecurityPlan(row.Name, role.RoleId, changes) { Covered = covered });
        }

        if (missing.Count > 0)
        {
            throw new PrivilegeNotFoundException(missing.Distinct().ToList());
        }

        var preview = new SecurityImportPreview(input.Table, plans);
        ThrowOnConflict(preview, $"'{input.Table}' security file");
        return preview;
    }

    public async Task<SecurityImportPreview> PreviewRoleAsync(Uri environmentUrl, string accessToken, RoleDefinition input, string solutionUniqueName, bool allowRemovals, CancellationToken cancellationToken)
    {
        var role = await FindRoleAsync(environmentUrl, accessToken, input.Name, cancellationToken);
        var solutionTables = await dataverseClient.TryGetSolutionEntityLogicalNamesAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
            ?? throw new SolutionNotFoundException(solutionUniqueName);
        var inSolution = new HashSet<string>(solutionTables, StringComparer.OrdinalIgnoreCase);

        var live = await dataverseClient.GetRolePrivilegesAsync(environmentUrl, accessToken, role.RoleId, cancellationToken);

        // What the file states, entry by entry: its non-table privileges, then the eight rights of every outside table it lists
        // (a solution table listed under tables: is ignored: its privileges belong to its own security file).
        var fileNamed = (input.Privileges ?? new Dictionary<string, SecurityAccessLevel>()).ToList();
        var fileTableEntries = (input.Tables ?? new Dictionary<string, TablePrivilegeLevels>())
            .Where(t => !inSolution.Contains(t.Key))
            .SelectMany(t => Enum.GetValues<TablePrivilege>().Select(right => (Table: t.Key, Right: right, Desired: SecurityPrivilegeLevels.Get(t.Value, right) ?? SecurityAccessLevel.None)))
            .ToList();

        // Every privilege involved (the file's and everything held live) is resolved before anything is planned, in one call.
        var identities = fileNamed.Select(p => PrivilegeIdentity.ForName(p.Key))
            .Concat(fileTableEntries.Select(e => PrivilegeIdentity.ForTable(e.Table, e.Right)))
            .Concat(live.Select(p => WriteIdentity(p.Privilege)))
            .ToList();
        var resolved = await dataverseClient.ResolvePrivilegesAsync(environmentUrl, accessToken, identities, cancellationToken);
        var namedResolved = resolved.Take(fileNamed.Count).ToList();
        var tableResolved = resolved.Skip(fileNamed.Count).Take(fileTableEntries.Count).ToList();
        var liveResolved = resolved.Skip(fileNamed.Count + fileTableEntries.Count).ToList();

        // What the role holds live, by privilege id: a privilege several tables share holds one level.
        var liveById = new Dictionary<Guid, SecurityAccessLevel>();
        for (var i = 0; i < live.Count; i++)
        {
            if (liveResolved[i] is { } privilege)
            {
                var level = SecurityAccessLevelMapping.FromDepth(live[i].Depth);
                liveById[privilege.PrivilegeId] = liveById.TryGetValue(privilege.PrivilegeId, out var other) && other > level ? other : level;
            }
        }

        SecurityAccessLevel From(Guid id) => liveById.TryGetValue(id, out var level) ? level : SecurityAccessLevel.None;

        var missing = new List<string>();
        var refusals = new List<string>();
        var changes = new List<PrivilegeChange>();
        var covered = new List<CoveredPrivilege>();
        var coveredIds = new HashSet<Guid>();

        for (var i = 0; i < fileNamed.Count; i++)
        {
            var (name, desired) = fileNamed[i];
            if (namedResolved[i] is not { } privilege)
            {
                if (desired != SecurityAccessLevel.None)
                {
                    missing.Add(name);
                }

                continue;
            }

            if (privilege.Tables.Count > 0)
            {
                var solutionOnes = SolutionTablesOf(privilege, inSolution);
                refusals.Add(solutionOnes.Count > 0
                    ? $"'{name}' under privileges: is a privilege of the solution's table(s) {string.Join(", ", solutionOnes)}; set it in that table's security file"
                    : $"'{name}' under privileges: is a table privilege ({string.Join(", ", privilege.Tables.Select(t => $"{t.Right} on {t.Table}"))}); list it under tables:");
                continue;
            }

            covered.Add(new CoveredPrivilege(privilege.PrivilegeId, privilege.Name, name, desired));
            coveredIds.Add(privilege.PrivilegeId);
            AddChange(changes, PrivilegeIdentity.ForName(name), privilege, ownTable: null, From(privilege.PrivilegeId), desired, allowRemovals);
        }

        for (var i = 0; i < fileTableEntries.Count; i++)
        {
            var (table, right, desired) = fileTableEntries[i];
            if (tableResolved[i] is not { } privilege)
            {
                if (desired != SecurityAccessLevel.None)
                {
                    missing.Add($"{right} on {table}");
                }

                continue;
            }

            var entry = $"{right} on {table}";
            var from = From(privilege.PrivilegeId);
            var solutionOnes = SolutionTablesOf(privilege, inSolution);
            if (solutionOnes.Count > 0 && from != desired)
            {
                refusals.Add($"{entry} is the privilege '{privilege.Name}', which {string.Join(", ", solutionOnes)} (a table of the solution) shares: changing it here would change that table, which only its own security file may do");
                continue;
            }

            covered.Add(new CoveredPrivilege(privilege.PrivilegeId, privilege.Name, entry, desired));
            coveredIds.Add(privilege.PrivilegeId);
            AddChange(changes, PrivilegeIdentity.ForTable(table, right), privilege, table, from, desired, allowRemovals);
        }

        // The full picture: anything the role holds outside the solution that the file doesn't state is a removal candidate - except a
        // privilege of (or shared with) a table of the solution, which belongs to that table's security file.
        for (var i = 0; i < live.Count; i++)
        {
            var identity = live[i].Privilege;
            if (liveResolved[i] is not { } privilege
                || coveredIds.Contains(privilege.PrivilegeId)
                || (identity.IsTablePrivilege && inSolution.Contains(identity.TableLogicalName!))
                || SolutionTablesOf(privilege, inSolution).Count > 0)
            {
                continue;
            }

            covered.Add(new CoveredPrivilege(privilege.PrivilegeId, privilege.Name, Describe(identity), SecurityAccessLevel.None));
            AddChange(changes, WriteIdentity(identity), privilege, identity.TableLogicalName, From(privilege.PrivilegeId), SecurityAccessLevel.None, allowRemovals);
        }

        if (missing.Count > 0)
        {
            throw new PrivilegeNotFoundException(missing.Distinct().ToList());
        }

        if (refusals.Count > 0)
        {
            throw new SecurityImportRefusedException($"Role file for '{input.Name}' refused: {string.Join("; ", refusals)}. Nothing was written.");
        }

        var preview = new SecurityImportPreview(input.Name, [new RoleSecurityPlan(input.Name, role.RoleId, changes) { Covered = covered }]);
        ThrowOnConflict(preview, $"role file for '{input.Name}'");
        return preview;
    }

    public async Task ApplyAsync(Uri environmentUrl, string accessToken, SecurityImportPreview preview, CancellationToken cancellationToken)
    {
        foreach (var plan in preview.Roles)
        {
            // A privilege several entries stand for (a shared one) is one write. They agree: a disagreement failed the preview.
            var adds = Distinct(plan.Changes.Where(c => c.Action == PrivilegeChangeAction.Set))
                .Select(c => new RolePrivilege(c.Privilege, SecurityAccessLevelMapping.ToDepth(c.To)
                    ?? throw new InvalidOperationException($"A set of {Describe(c.Privilege)} must have an access level other than None.")))
                .ToList();
            if (adds.Count > 0)
            {
                await dataverseClient.AddRolePrivilegesAsync(environmentUrl, accessToken, plan.RoleId, adds, cancellationToken);
            }

            var removals = Distinct(plan.Changes.Where(c => c.Action == PrivilegeChangeAction.Remove)).Select(c => c.Privilege).ToList();
            if (removals.Count > 0)
            {
                await dataverseClient.RemoveRolePrivilegesAsync(environmentUrl, accessToken, plan.RoleId, removals, cancellationToken);
            }
        }
    }

    /// <summary>How a privilege is named in the preview: its platform name, or "Right on table" for a table privilege.</summary>
    public static string Describe(PrivilegeIdentity privilege) =>
        privilege.IsTablePrivilege ? $"{privilege.TableRight} on {privilege.TableLogicalName}" : privilege.PrivilegeName ?? "(unnamed privilege)";

    /// <summary>How this tool asks the client to write a privilege: the table pair for a table privilege, the name alone for any other.</summary>
    private static PrivilegeIdentity WriteIdentity(PrivilegeIdentity privilege) =>
        privilege.IsTablePrivilege ? PrivilegeIdentity.ForTable(privilege.TableLogicalName!, privilege.TableRight!.Value) : PrivilegeIdentity.ForName(privilege.PrivilegeName!);

    private static List<string> SolutionTablesOf(ResolvedPrivilege privilege, HashSet<string> inSolution) =>
        privilege.Tables.Where(t => inSolution.Contains(t.Table)).Select(t => t.Table).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IEnumerable<PrivilegeChange> Distinct(IEnumerable<PrivilegeChange> changes) =>
        changes.GroupBy(c => c.PrivilegeId == Guid.Empty ? (object)c.Privilege : c.PrivilegeId).Select(g => g.First());

    private static void ThrowOnConflict(SecurityImportPreview preview, string source)
    {
        var conflicts = SecurityImportConflicts.Find([(source, preview)]);
        if (conflicts.Count > 0)
        {
            throw new SecurityConflictException(string.Join(" ", conflicts.Select(c => c.Message)));
        }
    }

    private async Task<SecurityRole> FindRoleAsync(Uri environmentUrl, string accessToken, string roleName, CancellationToken cancellationToken) =>
        await dataverseClient.TryGetRoleByNameAsync(environmentUrl, accessToken, roleName, cancellationToken)
            ?? throw new RoleNotFoundException(roleName);

    private static void AddChange(List<PrivilegeChange> changes, PrivilegeIdentity identity, ResolvedPrivilege privilege, string? ownTable, SecurityAccessLevel from, SecurityAccessLevel to, bool allowRemovals)
    {
        if (from == to)
        {
            return;
        }

        var action = to != SecurityAccessLevel.None
            ? PrivilegeChangeAction.Set
            : allowRemovals ? PrivilegeChangeAction.Remove : PrivilegeChangeAction.KeptNotRemoved;
        var alsoChanges = privilege.Tables
            .Select(t => t.Table)
            .Where(t => !string.Equals(t, ownTable, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
        changes.Add(new PrivilegeChange(identity, from, to, action) { PrivilegeId = privilege.PrivilegeId, AlsoChanges = alsoChanges });
    }
}

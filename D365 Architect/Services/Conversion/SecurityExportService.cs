using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class SecurityExportService(IDataverseClient dataverseClient) : ISecurityExportService
{
    public async Task<SecurityExportResult> ExportSecurityAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, IReadOnlyCollection<string>? tables, IReadOnlyCollection<string>? roles, CancellationToken cancellationToken)
    {
        var solutionTables = await dataverseClient.TryGetSolutionEntityLogicalNamesAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
            ?? throw new SolutionNotFoundException(solutionUniqueName);
        var solutionRoles = await dataverseClient.TryGetSolutionRolesAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
            ?? throw new SolutionNotFoundException(solutionUniqueName);

        var tablesGiven = tables is { Count: > 0 };
        var rolesGiven = roles is { Count: > 0 };

        var selectedTables = tablesGiven
            ? SolutionTableFilter.Select(solutionTables, tables!, $"in solution '{solutionUniqueName}'")
            : solutionTables;
        var selectedRoles = rolesGiven ? SelectRoles(solutionRoles, roles!, solutionUniqueName) : solutionRoles;

        // Neither filter = both kinds of file; one filter = only that kind; both = both, each limited.
        var writeTableFiles = tablesGiven || !rolesGiven;
        var writeRoleFiles = rolesGiven || !tablesGiven;

        // Table files list every role of the solution; role files need only the selected ones.
        var rolesToRead = writeTableFiles ? solutionRoles : selectedRoles;
        var held = new Dictionary<Guid, IReadOnlyList<RolePrivilege>>();
        foreach (var role in rolesToRead)
        {
            held[role.RoleId] = await dataverseClient.GetRolePrivilegesAsync(environmentUrl, accessToken, role.RoleId, cancellationToken);
        }

        var tableFiles = new List<ExportedTableSecurity>();
        if (writeTableFiles)
        {
            foreach (var table in selectedTables)
            {
                var rows = solutionRoles
                    .Select(role => ToTableRow(role.Name, held[role.RoleId], table))
                    .OfType<TableRoleSecurity>()
                    .ToList();

                // A table no role of the solution can reach gets no file at all, never an empty one.
                if (rows.Count > 0)
                {
                    tableFiles.Add(new ExportedTableSecurity(table, TableSecurityYamlSerializer.ToYaml(new TableSecurityDefinition { Table = table, Roles = rows })));
                }
            }
        }

        var roleFiles = new List<ExportedRole>();
        if (writeRoleFiles)
        {
            var inSolution = new HashSet<string>(solutionTables, StringComparer.OrdinalIgnoreCase);
            var usedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in selectedRoles)
            {
                var outside = await WithoutPrivilegesSharedWithSolutionTablesAsync(environmentUrl, accessToken, held[role.RoleId], inSolution, cancellationToken);
                var definition = ToRoleDefinition(role.Name, outside, inSolution);
                var stem = AssetFileNaming.MakeUnique(AssetFileNaming.Slugify(role.Name), usedStems);
                roleFiles.Add(new ExportedRole(stem, role.Name, RoleYamlSerializer.ToYaml(definition)));
            }
        }

        return new SecurityExportResult(tableFiles, roleFiles);
    }

    /// <summary>
    /// The role's privileges without those on outside tables that one id shares with a table of the solution (the activity tables share
    /// <c>prvCreateActivity</c>): that privilege is stated once, in the solution table's security file, never again in the role file.
    /// </summary>
    private async Task<IReadOnlyList<RolePrivilege>> WithoutPrivilegesSharedWithSolutionTablesAsync(Uri environmentUrl, string accessToken, IReadOnlyList<RolePrivilege> privileges, HashSet<string> inSolution, CancellationToken cancellationToken)
    {
        var outsideTable = privileges.Where(p => p.Privilege.IsTablePrivilege && !inSolution.Contains(p.Privilege.TableLogicalName!)).ToList();
        if (outsideTable.Count == 0)
        {
            return privileges;
        }

        var resolved = await dataverseClient.ResolvePrivilegesAsync(environmentUrl, accessToken,
            outsideTable.Select(p => PrivilegeIdentity.ForTable(p.Privilege.TableLogicalName!, p.Privilege.TableRight!.Value)).ToList(), cancellationToken);
        var shared = outsideTable.Where((_, i) => resolved[i]?.Tables.Any(t => inSolution.Contains(t.Table)) == true).ToHashSet();
        return privileges.Where(p => !shared.Contains(p)).ToList();
    }

    /// <summary>The solution's roles named in <paramref name="requested"/> (case-insensitive). Throws <see cref="UnknownRoleException"/> naming every requested role that is not one of them.</summary>
    internal static IReadOnlyList<SecurityRole> SelectRoles(IReadOnlyList<SecurityRole> solutionRoles, IReadOnlyCollection<string> requested, string solutionUniqueName)
    {
        var unmatched = requested
            .Where(r => !solutionRoles.Any(role => string.Equals(role.Name, r, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unmatched.Count > 0)
        {
            throw new UnknownRoleException(unmatched, solutionUniqueName);
        }

        return solutionRoles.Where(role => requested.Contains(role.Name, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The role's row for <paramref name="table"/>, or null when it holds none of the eight privileges on it.</summary>
    private static TableRoleSecurity? ToTableRow(string roleName, IReadOnlyList<RolePrivilege> privileges, string table)
    {
        var levels = SecurityPrivilegeLevels.OnTable(privileges, table);
        if (levels.Count == 0)
        {
            return null;
        }

        SecurityAccessLevel Level(TablePrivilege right) => levels.TryGetValue(right, out var level) ? level : SecurityAccessLevel.None;

        return new TableRoleSecurity
        {
            Name = roleName,
            Create = Level(TablePrivilege.Create),
            Read = Level(TablePrivilege.Read),
            Write = Level(TablePrivilege.Write),
            Delete = Level(TablePrivilege.Delete),
            Append = Level(TablePrivilege.Append),
            AppendTo = Level(TablePrivilege.AppendTo),
            Assign = Level(TablePrivilege.Assign),
            Share = Level(TablePrivilege.Share),
        };
    }

    /// <summary>The role file: its non-table privileges, and its privileges on every table that is not in the solution. Only what is held, sorted by name.</summary>
    private static RoleDefinition ToRoleDefinition(string roleName, IReadOnlyList<RolePrivilege> privileges, HashSet<string> solutionTables)
    {
        var named = new Dictionary<string, SecurityAccessLevel>();
        foreach (var (name, level) in SecurityPrivilegeLevels.NonTable(privileges).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            named[name] = level;
        }

        var outsideTables = new Dictionary<string, TablePrivilegeLevels>();
        var tableNames = privileges
            .Where(p => p.Privilege.IsTablePrivilege && !solutionTables.Contains(p.Privilege.TableLogicalName!))
            .Select(p => p.Privilege.TableLogicalName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);
        foreach (var table in tableNames)
        {
            var levels = SecurityPrivilegeLevels.OnTable(privileges, table);
            SecurityAccessLevel? Level(TablePrivilege right) => levels.TryGetValue(right, out var level) ? level : null;
            outsideTables[table] = new TablePrivilegeLevels
            {
                Create = Level(TablePrivilege.Create),
                Read = Level(TablePrivilege.Read),
                Write = Level(TablePrivilege.Write),
                Delete = Level(TablePrivilege.Delete),
                Append = Level(TablePrivilege.Append),
                AppendTo = Level(TablePrivilege.AppendTo),
                Assign = Level(TablePrivilege.Assign),
                Share = Level(TablePrivilege.Share),
            };
        }

        return new RoleDefinition
        {
            Name = roleName,
            Privileges = named.Count > 0 ? named : null,
            Tables = outsideTables.Count > 0 ? outsideTables : null,
        };
    }
}

/// <summary>Reads a role's held privileges (as <see cref="IDataverseClient.GetRolePrivilegesAsync"/> returns them) as YAML access levels. Shared by the security export and import.</summary>
internal static class SecurityPrivilegeLevels
{
    /// <summary>The access level of each of the eight privileges the role holds on <paramref name="table"/> (case-insensitive). A privilege not held has no entry.</summary>
    public static Dictionary<TablePrivilege, SecurityAccessLevel> OnTable(IEnumerable<RolePrivilege> privileges, string table) =>
        privileges
            .Where(p => p.Privilege.TableRight is not null && string.Equals(p.Privilege.TableLogicalName, table, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.Privilege.TableRight!.Value)
            .ToDictionary(g => g.Key, g => SecurityAccessLevelMapping.FromDepth(g.Max(p => p.Depth)));

    /// <summary>The access level of each non-table privilege the role holds, by platform name (case-insensitive keys).</summary>
    public static Dictionary<string, SecurityAccessLevel> NonTable(IEnumerable<RolePrivilege> privileges) =>
        privileges
            .Where(p => !p.Privilege.IsTablePrivilege && p.Privilege.PrivilegeName is not null)
            .GroupBy(p => p.Privilege.PrivilegeName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => SecurityAccessLevelMapping.FromDepth(g.Max(p => p.Depth)), StringComparer.OrdinalIgnoreCase);

    /// <summary>The access-level property of <paramref name="row"/> for <paramref name="right"/>.</summary>
    public static SecurityAccessLevel Get(TableRoleSecurity row, TablePrivilege right) => right switch
    {
        TablePrivilege.Create => row.Create,
        TablePrivilege.Read => row.Read,
        TablePrivilege.Write => row.Write,
        TablePrivilege.Delete => row.Delete,
        TablePrivilege.Append => row.Append,
        TablePrivilege.AppendTo => row.AppendTo,
        TablePrivilege.Assign => row.Assign,
        TablePrivilege.Share => row.Share,
        _ => throw new ArgumentOutOfRangeException(nameof(right), right, null),
    };

    /// <summary>The access-level property of <paramref name="levels"/> for <paramref name="right"/> (null = not listed = not held).</summary>
    public static SecurityAccessLevel? Get(TablePrivilegeLevels levels, TablePrivilege right) => right switch
    {
        TablePrivilege.Create => levels.Create,
        TablePrivilege.Read => levels.Read,
        TablePrivilege.Write => levels.Write,
        TablePrivilege.Delete => levels.Delete,
        TablePrivilege.Append => levels.Append,
        TablePrivilege.AppendTo => levels.AppendTo,
        TablePrivilege.Assign => levels.Assign,
        TablePrivilege.Share => levels.Share,
        _ => throw new ArgumentOutOfRangeException(nameof(right), right, null),
    };
}

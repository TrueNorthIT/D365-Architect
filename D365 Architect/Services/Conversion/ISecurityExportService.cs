namespace D365Architect.Services.Conversion;

/// <summary>
/// Exports the security roles of one solution as YAML: a <c>*.security.yml</c> per table (who can do what to it) and a <c>*.role.yml</c> per
/// role (its non-table privileges and its privileges on tables outside the solution). Composed by <see cref="ISolutionExportService"/> and used
/// directly by <c>security export</c>. Returns YAML in memory; the calling command writes the files, as for every other export service.
///
/// Only the solution's own roles are read (never another role, however much access it has), and each table privilege is reported in exactly
/// one place: a table in the solution in that table's file, any other table in the role's file.
///
/// Filters (<c>tables</c>, <c>roles</c>; null or empty means "not given"):
/// neither given = every table file and every role file; only <c>tables</c> = just those tables' files and NO role files;
/// only <c>roles</c> = just those roles' files and NO table files; both = both.
/// </summary>
public interface ISecurityExportService
{
    /// <param name="environmentUrl">The D365 environment to read from.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="solutionUniqueName">Unique name of the solution whose roles and tables are exported.</param>
    /// <param name="tables">Table logical names (matched case-insensitively) to limit the table files to.</param>
    /// <param name="roles">Role names (matched case-insensitively) to limit the role files to.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="Dataverse.SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution.</exception>
    /// <exception cref="UnknownTableException">A name in <paramref name="tables"/> is not a table in the solution.</exception>
    /// <exception cref="UnknownRoleException">A name in <paramref name="roles"/> is not a role of the solution.</exception>
    Task<SecurityExportResult> ExportSecurityAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, IReadOnlyCollection<string>? tables, IReadOnlyCollection<string>? roles, CancellationToken cancellationToken);
}

/// <param name="TableLogicalName">The table's logical name — also the sub-folder its security file goes in.</param>
/// <param name="Yaml">The <c>&lt;table&gt;.security.yml</c> text.</param>
public sealed record ExportedTableSecurity(string TableLogicalName, string Yaml);

/// <param name="FileNameStem">The role file's name without <c>.role.yml</c>: the role's name lower-cased and slugged, e.g. "HIS User" gives "his-user".</param>
/// <param name="RoleName">The role's own name.</param>
/// <param name="Yaml">The <c>&lt;stem&gt;.role.yml</c> text.</param>
public sealed record ExportedRole(string FileNameStem, string RoleName, string Yaml);

/// <param name="Tables">One entry per table that at least one of the solution's roles holds a privilege on (and that the filter allows). A table nobody has access to has no entry, so no file is written.</param>
/// <param name="Roles">One entry per role file to write: every role of the solution that the filter allows, even one that holds nothing else.</param>
public sealed record SecurityExportResult(IReadOnlyList<ExportedTableSecurity> Tables, IReadOnlyList<ExportedRole> Roles);

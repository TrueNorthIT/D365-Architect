using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Writes a table security file or a role file back into Dataverse by changing what each named role holds, after a preview of exactly what would change.
///
/// Roles are looked up by NAME in the target environment and are never created: a role the file names that doesn't exist makes the whole file's
/// preview throw <see cref="RoleNotFoundException"/> and nothing from that file is written. A role the file doesn't name is never touched.
///
/// Writes use add-privilege semantics, never replace-all: raising or lowering a level is one <see cref="IDataverseClient.AddRolePrivilegesAsync"/>
/// of just the privileges that differ, so every other privilege the role holds is untouched. A privilege held live but None or missing in the
/// file is only removed when <c>allowRemovals</c> is set; otherwise it is kept and reported (<see cref="PrivilegeChangeAction.KeptNotRemoved"/>).
///
/// Scope: a table file only ever compares the eight privileges on its own table for the roles it names. A role file is the FULL picture of the
/// role's privileges outside the solution: it compares its non-table privileges (every one the role holds live that the file doesn't list is a
/// removal candidate) and the privileges on EVERY table that is not one of the solution's tables (a table held live but absent from <c>tables:</c>,
/// or a privilege absent within a listed table, is a removal candidate). A privilege on one of the solution's own tables belongs to that table's
/// file: a role file never reports or removes it, even with <c>allowRemovals</c>.
/// </summary>
public interface ISecurityImportService
{
    /// <exception cref="RoleNotFoundException">A role named in the file doesn't exist in the environment.</exception>
    Task<SecurityImportPreview> PreviewTableSecurityAsync(Uri environmentUrl, string accessToken, TableSecurityDefinition input, bool allowRemovals, CancellationToken cancellationToken);

    /// <param name="environmentUrl">The D365 environment to compare with.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="input">The role file.</param>
    /// <param name="solutionUniqueName">The solution the role file belongs to. Its tables (resolved from Dataverse) are the ones a role file never touches; every other table is the role file's business.</param>
    /// <param name="allowRemovals">Whether a privilege held live but missing from the file is removed (otherwise kept and reported).</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="RoleNotFoundException">The role the file names doesn't exist in the environment.</exception>
    /// <exception cref="SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution.</exception>
    Task<SecurityImportPreview> PreviewRoleAsync(Uri environmentUrl, string accessToken, RoleDefinition input, string solutionUniqueName, bool allowRemovals, CancellationToken cancellationToken);

    /// <summary>Writes exactly what <paramref name="preview"/> planned: per role, one add of the <see cref="PrivilegeChangeAction.Set"/> changes and one remove of the <see cref="PrivilegeChangeAction.Remove"/> changes. Nothing else.</summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, SecurityImportPreview preview, CancellationToken cancellationToken);
}

/// <summary>What the import will do about one privilege that differs between the file and the live role.</summary>
public enum PrivilegeChangeAction
{
    /// <summary>Hold the privilege at <c>To</c> (raising, lowering, or newly holding it): an add-privilege write.</summary>
    Set,

    /// <summary>The role holds it live, the file doesn't, and removals are allowed: it will be removed.</summary>
    Remove,

    /// <summary>The role holds it live, the file doesn't, and removals are not allowed: it is kept and only reported ("would remove ... (kept)"). Writes nothing.</summary>
    KeptNotRemoved,
}

/// <param name="Privilege">Which privilege. For a table privilege the table and right are set; for a non-table one, the platform name.</param>
/// <param name="From">The access level the role holds live (<see cref="SecurityAccessLevel.None"/> when it doesn't hold it).</param>
/// <param name="To">The access level the file asks for (<see cref="SecurityAccessLevel.None"/> when the file leaves it out or says None).</param>
/// <param name="Action">What the import does about the difference.</param>
public sealed record PrivilegeChange(PrivilegeIdentity Privilege, SecurityAccessLevel From, SecurityAccessLevel To, PrivilegeChangeAction Action);

/// <param name="RoleName">The role's name, as in the file.</param>
/// <param name="RoleId">The role's id in THIS environment, found by name.</param>
/// <param name="Changes">Every privilege that differs, including kept ones; empty when the role already matches the file.</param>
public sealed record RoleSecurityPlan(string RoleName, Guid RoleId, IReadOnlyList<PrivilegeChange> Changes)
{
    /// <summary>True when there is at least one write to make (a Set or a Remove). A kept privilege is not a write.</summary>
    public bool HasWrites => Changes.Any(c => c.Action is PrivilegeChangeAction.Set or PrivilegeChangeAction.Remove);
}

/// <param name="SubjectName">What was previewed: the table's logical name for a table file, the role's name for a role file.</param>
/// <param name="Roles">One plan per role the file names, in file order.</param>
public sealed record SecurityImportPreview(string SubjectName, IReadOnlyList<RoleSecurityPlan> Roles)
{
    /// <summary>True when there is at least one write to make, so the file is "changed". A preview with only kept privileges is not.</summary>
    public bool HasChanges => Roles.Any(r => r.HasWrites);
}

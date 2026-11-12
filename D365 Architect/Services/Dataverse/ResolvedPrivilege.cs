namespace D365Architect.Services.Dataverse;

/// <summary>One table and right a privilege stands for.</summary>
public sealed record TablePrivilegeRef(string Table, TablePrivilege Right);

/// <summary>
/// A privilege as this environment knows it: its id, its platform name, and every table and right it stands for. A non-table privilege has
/// no tables. A table privilege usually has one, but a privilege can be shared: the activity tables (email, appointment, phonecall, ...)
/// all use the same <c>prvCreateActivity</c>, so changing it on one changes it on all of them.
/// </summary>
public sealed record ResolvedPrivilege(Guid PrivilegeId, string Name, IReadOnlyList<TablePrivilegeRef> Tables);

/// <summary>A role name that matches more than one role in the root business unit, so a file naming it can't say which one it means.</summary>
public sealed class DuplicateRoleNameException(string roleName, int count)
    : Exception($"Role name '{roleName}' matches {count} roles in this environment's root business unit; rename one so the file can say which it means.")
{
    public string RoleName { get; } = roleName;
}

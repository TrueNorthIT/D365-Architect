namespace D365Architect.Services.Dataverse;

/// <summary>
/// Which privilege something is about, independent of any environment's ids. A table privilege is told by
/// (<paramref name="TableLogicalName"/>, <paramref name="TableRight"/>); a non-table (miscellaneous) privilege by its platform
/// <paramref name="PrivilegeName"/> (e.g. <c>prvExportToExcel</c>). When <see cref="IDataverseClient.GetRolePrivilegesAsync"/> returns one,
/// <paramref name="PrivilegeName"/> is always set, and the table pair is set for a table privilege. When this tool asks the client to write one
/// (<see cref="IDataverseClient.AddRolePrivilegesAsync"/>/<see cref="IDataverseClient.RemoveRolePrivilegesAsync"/>) it gives the table pair for a
/// table privilege (no name: the client resolves it) or the name alone for a non-table one.
/// </summary>
public sealed record PrivilegeIdentity(string? PrivilegeName, string? TableLogicalName, TablePrivilege? TableRight)
{
    public bool IsTablePrivilege => TableLogicalName is not null;

    public static PrivilegeIdentity ForTable(string tableLogicalName, TablePrivilege right, string? privilegeName = null) => new(privilegeName, tableLogicalName, right);

    public static PrivilegeIdentity ForName(string privilegeName) => new(privilegeName, null, null);
}

/// <summary>One privilege a role holds (or is to hold) and how far it reaches.</summary>
public sealed record RolePrivilege(PrivilegeIdentity Privilege, PrivilegeDepth Depth);

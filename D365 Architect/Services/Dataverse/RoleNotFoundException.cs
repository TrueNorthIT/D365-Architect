namespace D365Architect.Services.Dataverse;

/// <summary>Thrown when a role named in a security file doesn't exist in the environment. Roles are never created by this tool.</summary>
public sealed class RoleNotFoundException(string roleName)
    : Exception($"Role '{roleName}' not found in this environment.")
{
    public string RoleName { get; } = roleName;
}

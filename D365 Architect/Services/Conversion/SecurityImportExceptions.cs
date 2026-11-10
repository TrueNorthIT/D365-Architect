namespace D365Architect.Services.Conversion;

/// <summary>Entries covering the same role and privilege ask for different access levels, so the privilege can't be written without one of them losing.</summary>
public sealed class SecurityConflictException(string message) : Exception(message);

/// <summary>A security file asks for something this tool refuses to do, e.g. a role file changing a privilege of one of the solution's own tables.</summary>
public sealed class SecurityImportRefusedException(string message) : Exception(message);

/// <summary>A privilege a security file asks for doesn't exist in the environment (e.g. Assign on an organisation-owned table).</summary>
public sealed class PrivilegeNotFoundException(IReadOnlyList<string> privileges)
    : Exception($"No such privilege in this environment: {string.Join(", ", privileges)}.")
{
    public IReadOnlyList<string> Privileges { get; } = privileges;
}

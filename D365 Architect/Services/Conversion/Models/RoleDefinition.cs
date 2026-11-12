using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated YAML shape for one security role's privileges that are not covered by a table's own <c>*.security.yml</c>: its non-table privileges,
/// and its privileges on tables outside the solution. Written to <c>&lt;solution&gt;/roles/&lt;role-name&gt;.role.yml</c>. Only privileges the
/// role holds are listed; a privilege left out is not held. A table in the solution never appears here: its privileges live in that table's
/// own <c>*.security.yml</c>.
/// </summary>
public sealed class RoleDefinition
{
    /// <summary>The role's name, e.g. "HIS User" — how the role is found in any environment.</summary>
    [YamlMember(Order = 0)]
    public required string Name { get; init; }

    /// <summary>The role's non-table privileges, by platform privilege name (e.g. "prvExportToExcel"), with their access level. Only held privileges are listed.</summary>
    [YamlMember(Order = 1)]
    public IReadOnlyDictionary<string, SecurityAccessLevel>? Privileges { get; init; }

    /// <summary>The role's privileges on tables that are not in the solution, keyed by table logical name. Only held privileges are listed.</summary>
    [YamlMember(Order = 2)]
    public IReadOnlyDictionary<string, TablePrivilegeLevels>? Tables { get; init; }
}

/// <summary>A role's privileges on one table outside the solution. A key left out means the privilege is not held.</summary>
public sealed class TablePrivilegeLevels
{
    /// <summary>Create access level.</summary>
    [YamlMember(Order = 0)]
    public SecurityAccessLevel? Create { get; init; }

    /// <summary>Read access level.</summary>
    [YamlMember(Order = 1)]
    public SecurityAccessLevel? Read { get; init; }

    /// <summary>Write access level.</summary>
    [YamlMember(Order = 2)]
    public SecurityAccessLevel? Write { get; init; }

    /// <summary>Delete access level.</summary>
    [YamlMember(Order = 3)]
    public SecurityAccessLevel? Delete { get; init; }

    /// <summary>Append access level.</summary>
    [YamlMember(Order = 4)]
    public SecurityAccessLevel? Append { get; init; }

    /// <summary>Append To access level.</summary>
    [YamlMember(Order = 5)]
    public SecurityAccessLevel? AppendTo { get; init; }

    /// <summary>Assign access level.</summary>
    [YamlMember(Order = 6)]
    public SecurityAccessLevel? Assign { get; init; }

    /// <summary>Share access level.</summary>
    [YamlMember(Order = 7)]
    public SecurityAccessLevel? Share { get; init; }
}

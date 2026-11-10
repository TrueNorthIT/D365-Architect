using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Curated YAML shape for who can do what to one table: the roles of a solution that hold any privilege on it, each with all eight table
/// privileges. Written to <c>&lt;solution&gt;/&lt;table&gt;/&lt;table&gt;.security.yml</c> by <c>solution export</c> and <c>security export</c>.
/// </summary>
public sealed class TableSecurityDefinition
{
    /// <summary>The table's logical name, e.g. "tn_complaint".</summary>
    [YamlMember(Order = 0)]
    public required string Table { get; init; }

    /// <summary>The roles that hold any privilege on this table.</summary>
    [YamlMember(Order = 1)]
    public required IReadOnlyList<TableRoleSecurity> Roles { get; init; }
}

/// <summary>
/// One role's eight privileges on a table. The role is identified by its name (never an id), so the file works in any environment.
/// An access level of <c>None</c> (or a key left out) means the role doesn't hold that privilege.
/// </summary>
public sealed class TableRoleSecurity
{
    /// <summary>The role's name, e.g. "HIS User".</summary>
    [YamlMember(Order = 0)]
    public required string Name { get; init; }

    /// <summary>Create access level.</summary>
    [YamlMember(Order = 1)]
    public SecurityAccessLevel Create { get; init; }

    /// <summary>Read access level.</summary>
    [YamlMember(Order = 2)]
    public SecurityAccessLevel Read { get; init; }

    /// <summary>Write access level.</summary>
    [YamlMember(Order = 3)]
    public SecurityAccessLevel Write { get; init; }

    /// <summary>Delete access level.</summary>
    [YamlMember(Order = 4)]
    public SecurityAccessLevel Delete { get; init; }

    /// <summary>Append access level.</summary>
    [YamlMember(Order = 5)]
    public SecurityAccessLevel Append { get; init; }

    /// <summary>Append To access level.</summary>
    [YamlMember(Order = 6)]
    public SecurityAccessLevel AppendTo { get; init; }

    /// <summary>Assign access level.</summary>
    [YamlMember(Order = 7)]
    public SecurityAccessLevel Assign { get; init; }

    /// <summary>Share access level.</summary>
    [YamlMember(Order = 8)]
    public SecurityAccessLevel Share { get; init; }
}

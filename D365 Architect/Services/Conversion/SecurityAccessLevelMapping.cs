using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>
/// The mapping between the YAML's <see cref="SecurityAccessLevel"/> and Dataverse's <see cref="PrivilegeDepth"/>:
/// User = Basic, BusinessUnit = Local, ParentChildBusinessUnit = Deep, Organisation = Global. <c>None</c> is "privilege not held" and has no depth.
/// </summary>
internal static class SecurityAccessLevelMapping
{
    /// <summary>The depth to hold the privilege at, or null for <see cref="SecurityAccessLevel.None"/> (not held).</summary>
    public static PrivilegeDepth? ToDepth(SecurityAccessLevel level) => level switch
    {
        SecurityAccessLevel.None => null,
        SecurityAccessLevel.User => PrivilegeDepth.Basic,
        SecurityAccessLevel.BusinessUnit => PrivilegeDepth.Local,
        SecurityAccessLevel.ParentChildBusinessUnit => PrivilegeDepth.Deep,
        SecurityAccessLevel.Organisation => PrivilegeDepth.Global,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown access level."),
    };

    public static SecurityAccessLevel FromDepth(PrivilegeDepth depth) => depth switch
    {
        PrivilegeDepth.Basic => SecurityAccessLevel.User,
        PrivilegeDepth.Local => SecurityAccessLevel.BusinessUnit,
        PrivilegeDepth.Deep => SecurityAccessLevel.ParentChildBusinessUnit,
        PrivilegeDepth.Global => SecurityAccessLevel.Organisation,
        _ => throw new ArgumentOutOfRangeException(nameof(depth), depth, "Unknown privilege depth."),
    };
}

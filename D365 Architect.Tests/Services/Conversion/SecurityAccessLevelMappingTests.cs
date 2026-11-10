using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #21 (security role privileges as YAML). Covers <see cref="SecurityAccessLevelMapping"/>, the one place that turns the YAML's access
/// level into Dataverse's privilege depth and back. Requirement text: "Access levels: None, User, BusinessUnit, ParentChildBusinessUnit,
/// Organisation (map to Dataverse privilege depth Basic/Local/Deep/Global; None = privilege not held)".
/// Tagged with the "Feature" trait "security-roles-yaml".
/// </summary>
[Trait("Feature", "security-roles-yaml")]
public sealed class SecurityAccessLevelMappingTests
{
    // Code path: SecurityAccessLevelMapping.ToDepth. The depth a privilege is written to Dataverse at.
    [Theory]
    [Trait("Feature", "security-roles-yaml")]
    [InlineData(SecurityAccessLevel.User, PrivilegeDepth.Basic)]
    [InlineData(SecurityAccessLevel.BusinessUnit, PrivilegeDepth.Local)]
    [InlineData(SecurityAccessLevel.ParentChildBusinessUnit, PrivilegeDepth.Deep)]
    [InlineData(SecurityAccessLevel.Organisation, PrivilegeDepth.Global)]
    public void ToDepth_MapsEachAccessLevelOntoItsDataverseDepth(SecurityAccessLevel level, PrivilegeDepth expected)
    {
        var depth = SecurityAccessLevelMapping.ToDepth(level);

        Expect.Equal<PrivilegeDepth?>(expected, depth, $"access level '{level}' should be written to Dataverse at depth '{expected}'.");
    }

    // "None = privilege not held": there is no depth to hold it at, so ToDepth gives null.
    [Fact]
    [Trait("Feature", "security-roles-yaml")]
    public void ToDepth_None_HasNoDepthBecauseThePrivilegeIsNotHeld()
    {
        var depth = SecurityAccessLevelMapping.ToDepth(SecurityAccessLevel.None);

        Expect.True(depth is null, $"access level 'None' means the privilege is not held, so it should have no depth. Actual: <{depth}>.");
    }

    // Code path: SecurityAccessLevelMapping.FromDepth. The access level a privilege read from Dataverse is exported as.
    [Theory]
    [Trait("Feature", "security-roles-yaml")]
    [InlineData(PrivilegeDepth.Basic, SecurityAccessLevel.User)]
    [InlineData(PrivilegeDepth.Local, SecurityAccessLevel.BusinessUnit)]
    [InlineData(PrivilegeDepth.Deep, SecurityAccessLevel.ParentChildBusinessUnit)]
    [InlineData(PrivilegeDepth.Global, SecurityAccessLevel.Organisation)]
    public void FromDepth_MapsEachDataverseDepthOntoItsAccessLevel(PrivilegeDepth depth, SecurityAccessLevel expected)
    {
        var level = SecurityAccessLevelMapping.FromDepth(depth);

        Expect.Equal(expected, level, $"Dataverse depth '{depth}' should be exported as access level '{expected}'.");
    }
}

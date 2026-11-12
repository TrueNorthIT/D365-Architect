namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// How far a security role's privilege reaches, as written in <c>*.security.yml</c> and <c>*.role.yml</c>. The member names are the YAML values.
/// <c>None</c> means the privilege isn't held; the others map onto Dataverse's privilege depth (see <see cref="Dataverse.PrivilegeDepth"/>):
/// User = Basic, BusinessUnit = Local, ParentChildBusinessUnit = Deep, Organisation = Global.
/// </summary>
public enum SecurityAccessLevel
{
    None,
    User,
    BusinessUnit,
    ParentChildBusinessUnit,
    Organisation,
}

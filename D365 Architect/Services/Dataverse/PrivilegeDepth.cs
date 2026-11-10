namespace D365Architect.Services.Dataverse;

/// <summary>
/// How far a held privilege reaches, in Dataverse's own terms. Maps one-to-one onto the YAML's
/// <see cref="Conversion.Models.SecurityAccessLevel"/> (except <c>None</c>, which means the privilege isn't held at all and so has no depth):
/// Basic = User, Local = BusinessUnit, Deep = ParentChildBusinessUnit, Global = Organisation.
/// </summary>
public enum PrivilegeDepth
{
    Basic,
    Local,
    Deep,
    Global,
}

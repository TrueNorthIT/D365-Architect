namespace D365Architect.Services.Dataverse;

/// <summary>The eight privileges a security role can hold on a table. Each is one key in a <c>*.security.yml</c> role row.</summary>
public enum TablePrivilege
{
    Create,
    Read,
    Write,
    Delete,
    Append,
    AppendTo,
    Assign,
    Share,
}

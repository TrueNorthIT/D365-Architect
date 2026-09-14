using System.Text.RegularExpressions;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// The customization-prefix naming shape Dataverse requires of a
/// custom-solution-owned schema name — shared by everything this tool
/// creates that needs one: a column's own <c>SchemaName</c>, a new Lookup's
/// <c>RelationshipSchemaName</c> (see <see cref="AttributeChangeValidator"/>),
/// and a global choice's own <c>Name</c> (see <c>GlobalChoiceChangeValidator</c>).
/// </summary>
internal static class DataverseSchemaNaming
{
    /// <summary>
    /// A short alphanumeric prefix, an underscore, then the rest of the
    /// name using only letters/digits/underscores throughout — e.g.
    /// <c>new_BankName</c>, <c>cr7a3_Account_Rating</c>,
    /// <c>new_contact_new_bankaccount</c>. This is the structural shape of
    /// every custom SchemaName confirmed live this session, not a guess at
    /// Dataverse's own exact validation regex or an attempt to check it
    /// against a specific registered publisher (that would need a live
    /// lookup this tool doesn't do) — it exists to catch the obvious
    /// mistakes (no prefix at all, or a space/dash/other character
    /// Dataverse's own schema name rules don't allow) before Dataverse does.
    /// Full-string match.
    /// </summary>
    public static readonly Regex SchemaNamePattern = new(@"^[A-Za-z][A-Za-z0-9]*_[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
}

namespace D365Architect.Services.Dataverse;

/// <summary>
/// What a table's live metadata says about itself that a view's layout needs
/// (see <see cref="IDataverseClient.TryGetEntityIdentityAsync"/>).
/// </summary>
/// <param name="ObjectTypeCode">The table's type code: the <c>object</c> attribute of a layout's <c>&lt;grid&gt;</c>.</param>
/// <param name="PrimaryIdAttribute">The primary id column's logical name: a layout's <c>row id</c>.</param>
/// <param name="PrimaryNameAttribute">The primary name column's logical name: a layout's <c>jump</c>.</param>
public sealed record EntityIdentity(int ObjectTypeCode, string PrimaryIdAttribute, string PrimaryNameAttribute);

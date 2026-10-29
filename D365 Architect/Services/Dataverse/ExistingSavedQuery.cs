namespace D365Architect.Services.Dataverse;

/// <summary>
/// A live <c>savedquery</c>'s id and the fields <c>view import</c> can
/// actually update — <c>description</c>, <c>fetchxml</c>, <c>layoutxml</c>.
/// Not every field <see cref="Conversion.Models.ViewDefinition"/> captures:
/// <c>querytype</c>/<c>isdefault</c>/<c>isquickfindquery</c> are all
/// documented on that model itself as fields applying a YAML file back
/// won't change, so there's nothing for import to read here for them.
/// </summary>
/// <param name="SavedQueryId">The view's id.</param>
/// <param name="Description">Live description.</param>
/// <param name="FetchXml">Live FetchXML.</param>
/// <param name="LayoutXml">Live LayoutXML.</param>
/// <param name="EntityLogicalName">
/// The live <c>returnedtypecode</c>. Only populated by
/// <see cref="IDataverseClient.TryGetSavedQueryByIdAsync"/>, which looks a view
/// up by id alone, so the caller can check it against the YAML's own
/// <c>entity</c>. Null from the by-name lookup, which already filtered by table.
/// </param>
/// <param name="Name">The live view name, from the same by-id lookup (null otherwise).</param>
public sealed record ExistingSavedQuery(Guid SavedQueryId, string? Description, string? FetchXml, string? LayoutXml, string? EntityLogicalName = null, string? Name = null);

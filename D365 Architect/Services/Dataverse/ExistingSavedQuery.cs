namespace D365Architect.Services.Dataverse;

/// <summary>
/// A live <c>savedquery</c>'s id and the fields <c>view import</c> can
/// actually update — <c>description</c>, <c>fetchxml</c>, <c>layoutxml</c>.
/// Not every field <see cref="Conversion.Models.ViewDefinition"/> captures:
/// <c>querytype</c>/<c>isdefault</c>/<c>isquickfindquery</c> are all
/// documented on that model itself as fields applying a YAML file back
/// won't change, so there's nothing for import to read here for them.
/// </summary>
public sealed record ExistingSavedQuery(Guid SavedQueryId, string? Description, string? FetchXml, string? LayoutXml);

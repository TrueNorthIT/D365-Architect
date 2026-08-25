namespace D365Architect.Services.Dataverse;

/// <summary>
/// A live <c>systemform</c>'s id and current <c>formxml</c> together — what
/// <c>form import</c> needs and <c>form build-xml</c> doesn't: build-xml
/// only ever reads a form's FormXML to patch onto it and writes a local
/// file, so it never needed the id itself (see
/// <see cref="IDataverseClient.TryGetSystemFormXmlAsync"/>). Import has to
/// know which record to update, hence this pairs both together rather than
/// adding a second round trip.
/// </summary>
public sealed record ExistingSystemForm(Guid FormId, string FormXml);

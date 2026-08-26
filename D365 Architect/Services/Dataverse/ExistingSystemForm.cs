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
/// <param name="FormId">The systemform's own id.</param>
/// <param name="FormXml">Its current, live <c>formxml</c>.</param>
/// <param name="EntityLogicalName">
/// Only populated by <see cref="IDataverseClient.TryGetSystemFormByIdAsync"/>,
/// which looks a form up by id alone with no table to already confirm it
/// against — lets <see cref="Conversion.FormImportService"/> warn when a
/// YAML's own <c>Entity</c>/<c>Name</c> have drifted from what the id
/// actually points at (e.g. copied into the wrong file). Null from
/// <see cref="IDataverseClient.TryGetSystemFormAsync"/>, which already
/// filtered by table + name, so there's nothing left to compare.
/// </param>
/// <param name="Name">As <see cref="EntityLogicalName"/>, from the same by-id lookup.</param>
public sealed record ExistingSystemForm(Guid FormId, string FormXml, string? EntityLogicalName = null, string? Name = null);

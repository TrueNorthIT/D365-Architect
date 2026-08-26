using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// A live <c>systemform</c>'s id and current <c>formxml</c> together —
/// returned by every form lookup this tool does, including
/// <see cref="IDataverseClient.TryGetSystemFormAsync"/> (by table + name)
/// and <see cref="IDataverseClient.TryGetSystemFormByIdAsync"/> (by id
/// directly, preferred by both <c>form import</c> and <c>form
/// build-xml</c> whenever the YAML has one — see
/// <see cref="Conversion.Models.FormDefinition.FormId"/>'s own doc
/// comment for why an id disambiguates what a name alone can't, e.g.
/// several forms sharing a display name).
/// </summary>
/// <param name="FormId">The systemform's own id.</param>
/// <param name="FormXml">Its current, live <c>formxml</c>.</param>
/// <param name="EntityLogicalName">
/// Only populated by <see cref="IDataverseClient.TryGetSystemFormByIdAsync"/>,
/// which looks a form up by id alone with no table to already confirm it
/// against — lets <see cref="BuildIdentityMismatchWarning"/> warn when a
/// YAML's own <c>Entity</c>/<c>Name</c> have drifted from what the id
/// actually points at (e.g. copied into the wrong file). Null from
/// <see cref="IDataverseClient.TryGetSystemFormAsync"/>, which already
/// filtered by table + name, so there's nothing left to compare.
/// </param>
/// <param name="Name">As <see cref="EntityLogicalName"/>, from the same by-id lookup.</param>
public sealed record ExistingSystemForm(Guid FormId, string FormXml, string? EntityLogicalName = null, string? Name = null)
{
    /// <summary>
    /// Warns when this record was resolved purely by id and the live
    /// table/name no longer match <paramref name="form"/>'s own — null
    /// whenever this wasn't a by-id lookup at all (see
    /// <see cref="EntityLogicalName"/>'s own doc comment) or when they do
    /// match. Shared by <c>form import</c> and <c>form build-xml</c>, the
    /// two callers of <see cref="IDataverseClient.TryGetSystemFormByIdAsync"/>,
    /// so neither has its own copy of this comparison to drift out of
    /// sync with the other. Never a reason to refuse anything — the id is
    /// still what's authoritative, this is just worth a human's attention.
    /// </summary>
    public string? BuildIdentityMismatchWarning(FormDefinition form)
    {
        if (EntityLogicalName is null && Name is null)
        {
            return null;
        }

        var entityMismatch = EntityLogicalName is not null && !string.Equals(EntityLogicalName, form.Entity, StringComparison.OrdinalIgnoreCase);
        var nameMismatch = Name is not null && Name != form.Name;

        if (!entityMismatch && !nameMismatch)
        {
            return null;
        }

        return $"This YAML's FormId resolves to '{Name}' on '{EntityLogicalName}', not this file's own '{form.Name}' on '{form.Entity}' — " +
            "the id is still what's being used (it's authoritative, not the name), but double-check this is the file you meant.";
    }
}

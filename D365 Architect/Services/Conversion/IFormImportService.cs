using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Writes a curated <see cref="FormDefinition"/>'s rebuilt FormXML back into
/// Dataverse — directly from the YAML, not through <c>form build-xml</c>:
/// that command exists for a human to inspect/validate the rebuilt FormXML
/// locally, not as a required step import goes through first. This service
/// does its own retrieve-and-patch independently, the same way
/// <see cref="FormXmlWriter"/> and <see cref="FormXmlValidator"/> are used
/// there, but never shares state or a call path with it.
///
/// Only ever updates a form that already exists — by its <c>FormId</c> when
/// the YAML has one (the ordinary case, and immune to a rename or a shared
/// name — see <see cref="FormDefinition.FormId"/>'s own doc comment), or by
/// table + name as a fallback for a file exported before that field
/// existed; creating a brand-new form isn't supported yet (see
/// <see cref="Dataverse.FormNotFoundException"/>).
/// </summary>
public interface IFormImportService
{
    /// <summary>
    /// Looks up the form, rebuilds its FormXML, and validates it — without
    /// writing anything, so the caller can show a diff and validation
    /// warnings and get explicit confirmation before <see cref="ApplyAsync"/>.
    /// </summary>
    /// <exception cref="Dataverse.FormNotFoundException">No form matches <c>form.FormId</c> (or, lacking one, <c>form.Entity</c>/<c>form.Name</c>).</exception>
    /// <exception cref="Dataverse.AmbiguousSystemFormException"><c>form.FormId</c> is absent and more than one form matches that table + name.</exception>
    /// <exception cref="NotSupportedException"><paramref name="form"/> is a dashboard.</exception>
    Task<FormImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, FormDefinition form, CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="preview"/>'s already-built FormXML back to the
    /// same form it was previewed against, then publishes it — see
    /// <see cref="Dataverse.IDataverseClient.UpdateSystemFormXmlAsync"/> and
    /// <see cref="Dataverse.IDataverseClient.PublishEntityAsync"/>.
    /// </summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, FormImportPreview preview, CancellationToken cancellationToken);
}

/// <summary>
/// The result of <see cref="IFormImportService.PreviewAsync"/> — everything
/// <see cref="IFormImportService.ApplyAsync"/> needs
/// (<see cref="FormId"/>/<see cref="NewFormXml"/>), plus everything a human
/// needs to decide whether to call it.
/// </summary>
/// <param name="FormId">The systemform's id — what <see cref="IFormImportService.ApplyAsync"/> updates.</param>
/// <param name="Entity">
/// The form's owning table's logical name — the live one when the form was
/// resolved by id (see <see cref="Dataverse.ExistingSystemForm.EntityLogicalName"/>),
/// falling back to the YAML's own <see cref="FormDefinition.Entity"/>
/// otherwise. What <see cref="IFormImportService.ApplyAsync"/> passes to
/// <see cref="Dataverse.IDataverseClient.PublishEntityAsync"/> after
/// writing the new FormXML.
/// </param>
/// <param name="ExistingFormXml">The form's real, current, unmodified live FormXML, exactly as Dataverse returned it.</param>
/// <param name="NewFormXml">The rebuilt FormXML <see cref="IFormImportService.ApplyAsync"/> will write — <see cref="FormXmlWriter"/>'s output, patched onto <paramref name="ExistingFormXml"/>.</param>
/// <param name="ExistingComparableFormXml">
/// <paramref name="ExistingFormXml"/> rebuilt through <see cref="FormXmlWriter"/>
/// from its own decomposed content — the same writer, the same base
/// document (<paramref name="ExistingFormXml"/> itself), and the same
/// deterministic id rules that produced <paramref name="NewFormXml"/>. This
/// — not <paramref name="ExistingFormXml"/> itself — is what actually gets
/// diffed against <paramref name="NewFormXml"/> before confirming.
///
/// Diffing the two *raw* documents directly was tried first and confirmed
/// live, on a real, richly-customized production form, to be nearly
/// useless: every tab, section, and cell showed up as "changed", purely
/// because their wrapper ids are resynthesized fresh on every rebuild (see
/// `docs/yaml-conventions.md`) — noise, not signal, even when re-importing
/// a file with no meaningful edits at all. Rebuilding *both* sides through
/// the identical pipeline cancels that noise out: unchanged content
/// produces the same ids and the same attribute-stripping on both sides
/// (so it disappears from the diff entirely), and only a genuine
/// difference in the underlying <see cref="FormDefinition"/> content
/// survives to show up — the same reason `terraform plan` diffs against a
/// normalized view of current state rather than a provider's raw
/// last-applied payload.
/// </param>
/// <param name="Violations"><see cref="FormXmlValidator"/>'s findings against <paramref name="NewFormXml"/>.</param>
/// <param name="IdentityMismatchWarning">
/// Set when the YAML's own <c>FormId</c> resolved to a live form whose
/// table and/or name no longer match the YAML's <c>Entity</c>/<c>Name</c> —
/// most likely the id was copied into the wrong file, or the form was
/// renamed live since this YAML was last exported. Never blocks the
/// import (the id is still authoritative — see
/// <see cref="Dataverse.IDataverseClient.TryGetSystemFormByIdAsync"/>), but
/// worth a human's attention before confirming. Null whenever the lookup
/// was by table + name instead (nothing to compare against) or when it
/// matched.
/// </param>
public sealed record FormImportPreview(Guid FormId, string Entity, string ExistingFormXml, string NewFormXml, string ExistingComparableFormXml, IReadOnlyList<FormXmlValidationMessage> Violations, string? IdentityMismatchWarning = null)
{
    /// <summary>False when <see cref="ExistingComparableFormXml"/> and <see cref="NewFormXml"/> are identical — see that parameter's own doc comment for why comparing those two, not the raw live document, is what makes this accurate rather than "true" almost every time.</summary>
    public bool HasChanges => ExistingComparableFormXml != NewFormXml;
}

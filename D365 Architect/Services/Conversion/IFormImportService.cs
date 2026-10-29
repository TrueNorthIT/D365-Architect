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
/// Matches the live form by its <c>FormId</c> when the YAML has one (the
/// ordinary case for an exported file, and immune to a rename or a shared
/// name — see <see cref="FormDefinition.FormId"/>'s own doc comment), or
/// otherwise by table + name + type (<c>type</c> absent means Main,
/// which is what tells apart the three forms named "Information" every new
/// custom table gets). A hand-written file that matches nothing that way is
/// created (<see cref="FormImportPreview.IsCreate"/>); a stale <c>FormId</c>
/// never is.
///
/// <see cref="IImportService{TInput,TPreview}.PreviewAsync"/> throws
/// <see cref="Dataverse.FormNotFoundException"/> when <c>form.FormId</c>
/// matches nothing, <see cref="Dataverse.AmbiguousSystemFormException"/>
/// (listing the candidate ids) when <c>form.FormId</c> is absent and
/// more than one form matches that table + name + type,
/// <see cref="InvalidDataException"/> for a <c>type</c> that isn't one of
/// Dataverse's, and <see cref="NotSupportedException"/> when the form is a
/// dashboard.
///
/// <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> only writes the
/// rebuilt FormXML (see
/// <see cref="Dataverse.IDataverseClient.UpdateSystemFormXmlAsync(Uri, string, Guid, string, CancellationToken)"/>) — it
/// deliberately doesn't publish. Publishing the form's owning table is
/// <see cref="PublishAsync"/>'s own, separate step instead, as for every
/// other import: `form import` calls it right after <c>ApplyAsync</c>
/// (unless <c>--no-publish</c>) rather than it happening as a hidden side
/// effect of the write.
/// </summary>
public interface IFormImportService : IImportService<FormDefinition, FormImportPreview>
{
    /// <summary>Publishes <paramref name="preview"/>'s owning table, making a just-applied change visible to end users without a separate manual publish step.</summary>
    Task PublishAsync(Uri environmentUrl, string accessToken, FormImportPreview preview, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>, adding whatever it creates or changes
    /// to <paramref name="solutionUniqueName"/> (null = no solution, sent as <c>MSCRM.SolutionUniqueName</c>),
    /// and creating the form when <paramref name="preview"/> is <see cref="FormImportPreview.IsCreate"/>
    /// — see <see cref="Dataverse.IDataverseClient.CreateSystemFormAsync"/> for the body.
    /// </summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, FormImportPreview preview, string? solutionUniqueName, CancellationToken cancellationToken);
}

/// <summary>
/// The result of <see cref="IImportService{TInput,TPreview}.PreviewAsync"/> — everything
/// <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> needs
/// (<see cref="FormId"/>/<see cref="NewFormXml"/>), plus everything a human
/// needs to decide whether to call it.
/// </summary>
/// <param name="FormId">The systemform's id — what <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> updates.</param>
/// <param name="Entity">
/// The form's owning table's logical name — the live one when the form was
/// resolved by id (see <see cref="Dataverse.ExistingSystemForm.EntityLogicalName"/>),
/// falling back to the YAML's own <see cref="FormDefinition.Entity"/>
/// otherwise. What <see cref="IFormImportService.PublishAsync"/> passes to
/// <see cref="Dataverse.IDataverseClient.PublishEntityAsync"/> after
/// <c>ApplyAsync</c> writes the new FormXML.
/// </param>
/// <param name="ExistingFormXml">The form's real, current, unmodified live FormXML, exactly as Dataverse returned it.</param>
/// <param name="NewFormXml">The rebuilt FormXML <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> will write — <see cref="FormXmlWriter"/>'s output, patched onto <paramref name="ExistingFormXml"/>.</param>
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
    public bool HasChanges => IsCreate || ExistingComparableFormXml != NewFormXml;

    /// <summary>
    /// True when no existing form matched the YAML (no <c>FormId</c>, and no form of that name and type on that
    /// table), so <c>ApplyAsync</c> creates one. Then <see cref="FormId"/> is <see cref="Guid.Empty"/>,
    /// <see cref="Entity"/> is the YAML's own table, the existing XML is empty and <see cref="HasChanges"/> is true.
    /// </summary>
    public bool IsCreate { get; init; }

    /// <summary>The YAML's form name — the create's <c>name</c>.</summary>
    public string Name { get; init; } = "";

    /// <summary>The systemform <c>type</c> option-set value matched on (and, for a create, sent): 2 Main, 6 Quick View Form, 7 Quick Create, 11 Card, ...</summary>
    public int FormType { get; init; } = 2;

    /// <summary>The YAML's description — sent on a create only (null = left out of the body); an update writes FormXML alone, as before.</summary>
    public string? Description { get; init; }
}

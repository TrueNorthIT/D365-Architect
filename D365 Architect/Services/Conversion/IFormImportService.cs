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

    /// <summary>
    /// As <see cref="IImportService{TInput,TPreview}.PreviewAsync"/>, but with
    /// <paramref name="allowRemovals"/> set, anything live that's missing from
    /// the YAML (and not simply moved) is removed rather than kept.
    /// </summary>
    Task<FormImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, FormDefinition form, bool allowRemovals, CancellationToken cancellationToken);
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
/// <paramref name="ExistingFormXml"/> itself, only its formatting normalised
/// the way <paramref name="NewFormXml"/> is written — what gets diffed
/// against <paramref name="NewFormXml"/> before confirming.
///
/// This used to be the live form rebuilt through the same writer as
/// <paramref name="NewFormXml"/>, which cancelled out the noise of every id
/// being regenerated. It also cancelled out everything the writer lost —
/// spacers, section label settings, header layout — so a form could be
/// damaged with a clean-looking diff (issue #58). Now that the writer
/// patches the live form instead of rebuilding it, unchanged content is
/// byte-identical anyway, and the diff is against the real thing.
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
    /// <summary>False when <see cref="ExistingComparableFormXml"/> (the live form) and <see cref="NewFormXml"/> are identical.</summary>
    public bool HasChanges => IsCreate || ExistingComparableFormXml != NewFormXml;

    /// <summary>
    /// What the patch changed, split by <see cref="FormPatchVerifier"/> into
    /// what the YAML asked for and what it didn't, plus what is live but
    /// missing from the YAML. Null for a create: there's no live form to compare.
    /// </summary>
    public FormPatchReport? PatchReport { get; init; }

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

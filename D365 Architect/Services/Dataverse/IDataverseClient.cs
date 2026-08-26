using System.Text.Json.Nodes;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thin wrapper over the Dataverse Web API. Takes an already-issued access
/// token per call rather than owning authentication itself — that's
/// <see cref="Authentication.IAuthenticationService"/>'s job.
/// </summary>
public interface IDataverseClient
{
    Task<WhoAmIResult> WhoAmIAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken);

    /// <summary>Best-effort lookup; returns null rather than throwing if it fails.</summary>
    Task<string?> TryGetUserFullNameAsync(Uri environmentUrl, string accessToken, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a table's metadata (including its columns) from the Web API's
    /// EntityDefinitions endpoint, as raw JSON — the shape
    /// <see cref="Conversion.EntityJsonDefinitionReader"/> reads.
    /// </summary>
    Task<string> GetEntityDefinitionJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a solution's unique name to the MetadataIds of the Attribute
    /// components it contains — i.e. which columns that solution actually
    /// customizes, as opposed to a table's full merged metadata. Returns
    /// null if no solution with that unique name exists.
    /// </summary>
    Task<IReadOnlySet<Guid>?> TryGetSolutionAttributeMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every view (<c>savedquery</c>) defined against a table from
    /// the Web API, as raw JSON — the shape
    /// <see cref="Conversion.ViewJsonDefinitionReader"/> reads.
    /// </summary>
    Task<string> GetViewDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a solution's unique name to the savedqueryids of the View
    /// components it contains — i.e. which views that solution actually
    /// customizes, as opposed to every view on a table. Returns null if no
    /// solution with that unique name exists.
    /// </summary>
    Task<IReadOnlySet<Guid>?> TryGetSolutionSavedQueryIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every form (<c>systemform</c>) defined against a table from
    /// the Web API, as raw JSON — the shape
    /// <see cref="Conversion.FormJsonDefinitionReader"/> reads.
    /// </summary>
    Task<string> GetFormDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches just enough about every form on a table to list them — id,
    /// name, type — without pulling each one's full <c>formxml</c>. Used to
    /// populate <c>form export</c>'s interactive picker before committing to
    /// decomposing (and, for a large form, transferring) the one actually
    /// chosen. See <see cref="Conversion.FormJsonDefinitionReader.ReadSummaries(string, IReadOnlySet{Guid}?)"/>.
    /// </summary>
    Task<string> GetFormSummariesJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a solution's unique name to the formids of the System Form
    /// components it contains — i.e. which forms that solution actually
    /// customizes, as opposed to every form on a table. Returns null if no
    /// solution with that unique name exists.
    /// </summary>
    Task<IReadOnlySet<Guid>?> TryGetSolutionSystemFormIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a single form's current, live <c>formxml</c> by table +
    /// display name — the fallback identity for a <c>*.form.yml</c>
    /// exported before <see cref="Conversion.Models.FormDefinition.FormId"/>
    /// existed; <c>form build-xml</c> uses it unconditionally, since it only
    /// ever patches onto the live document to write a local file and has
    /// never needed a form's id at all. Returns null when no form by that
    /// name exists yet on that table — the expected case for a form this
    /// tool's YAML describes but that hasn't been created in Dataverse yet.
    /// </summary>
    /// <exception cref="AmbiguousSystemFormException">More than one form on <paramref name="entityLogicalName"/> is named <paramref name="formName"/> — this tool won't guess which one to patch.</exception>
    Task<string?> TryGetSystemFormXmlAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="TryGetSystemFormXmlAsync"/>, but also returns the
    /// form's own id — what <c>form import</c> needs (to know which record
    /// to update) that <c>form build-xml</c> never did (it only ever writes
    /// a local file). See <see cref="ExistingSystemForm"/>. Used by
    /// <c>form import</c> only as a fallback, for a <c>*.form.yml</c> with
    /// no <c>FormId</c> of its own yet — see
    /// <see cref="TryGetSystemFormByIdAsync"/> for the ordinary, preferred
    /// path.
    /// </summary>
    /// <exception cref="AmbiguousSystemFormException">More than one form on <paramref name="entityLogicalName"/> is named <paramref name="formName"/> — this tool won't guess which one to import onto.</exception>
    Task<ExistingSystemForm?> TryGetSystemFormAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a single form directly by its <c>formid</c> — what
    /// <c>form import</c> prefers whenever the local YAML has one (see
    /// <see cref="Conversion.Models.FormDefinition.FormId"/>), since an id
    /// can't go ambiguous or stale the way a table + display name lookup
    /// can (a rename, or two forms sharing a name). Also returns the live
    /// record's own table/name (see <see cref="ExistingSystemForm.EntityLogicalName"/>/
    /// <see cref="ExistingSystemForm.Name"/>) so a caller can flag it if
    /// they've drifted from the YAML's own <c>Entity</c>/<c>Name</c> — a
    /// sign the id was copied into the wrong file, since nothing else here
    /// would otherwise catch that. Returns null when no form has that id —
    /// most likely it was deleted since this YAML was last exported.
    /// </summary>
    Task<ExistingSystemForm?> TryGetSystemFormByIdAsync(Uri environmentUrl, string accessToken, Guid formId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing <c>systemform</c>'s <c>formxml</c> via a PATCH
    /// request — the actual write <c>form import</c> performs. Doesn't
    /// publish the change: Dataverse customizations still need publishing
    /// separately before this is visible to end users (a deliberate,
    /// documented gap in this first cut — see `docs/yaml-conventions.md`).
    /// Doesn't check for a concurrent modification either (no ETag/If-Match)
    /// — see the same doc for what "checking differences" does and doesn't
    /// cover today.
    /// </summary>
    Task UpdateSystemFormXmlAsync(Uri environmentUrl, string accessToken, Guid formId, string formXml, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a single view's id, description, fetchxml, and layoutxml
    /// together by table + display name — the same identity <c>view
    /// export</c> uses (see <see cref="Conversion.Models.ViewDefinition"/>'s
    /// own doc comment on why <c>savedqueryid</c> isn't part of this tool's
    /// YAML). Returns null when no view by that name exists yet on that
    /// table.
    /// </summary>
    /// <exception cref="AmbiguousSavedQueryException">More than one view on <paramref name="entityLogicalName"/> is named <paramref name="viewName"/> — this tool won't guess which one to update.</exception>
    Task<ExistingSavedQuery?> TryGetSavedQueryAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string viewName, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing <c>savedquery</c>'s <c>description</c>/
    /// <c>fetchxml</c>/<c>layoutxml</c> via a PATCH request — the actual
    /// write <c>view import</c> performs. Only the non-null arguments are
    /// included in the request, matching this tool's own "an absent field
    /// means don't touch it" convention (see `docs/yaml-conventions.md`
    /// Rule 1) — pass null for anything the local YAML didn't have, not an
    /// empty string, or it would be cleared rather than left alone. Doesn't
    /// publish the change; see <see cref="UpdateSystemFormXmlAsync"/> for
    /// the same, already-documented gap.
    /// </summary>
    Task UpdateSavedQueryAsync(Uri environmentUrl, string accessToken, Guid savedQueryId, string? description, string? fetchXml, string? layoutXml, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches a table's own metadata — no <c>$expand=Attributes</c>, unlike
    /// <see cref="GetEntityDefinitionJsonAsync"/> — so the result is a clean
    /// round-trippable <c>EntityMetadata</c> object <c>table import</c> can
    /// mutate a couple of fields on and PUT straight back, rather than one
    /// carrying a navigation property (the expanded attribute collection)
    /// that Dataverse's own update API was never asked to accept back.
    /// </summary>
    Task<string> GetEntityMetadataJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a table's own metadata via a full-object PUT — Dataverse's
    /// documented update mechanism for entity/attribute definitions alike;
    /// there's no partial-update PATCH for these. <paramref name="entityMetadata"/>
    /// should be <see cref="GetEntityMetadataJsonAsync"/>'s own result,
    /// parsed and mutated in place — see <see cref="AttributeMetadataJsonBuilder"/>'s
    /// own doc comment for why a full-object round trip, not a freshly
    /// built partial body, is how this has to work. Sends
    /// <c>MSCRM.MergeLabels: true</c> so an edited display name doesn't wipe
    /// out other languages' labels this tool never touched. Doesn't publish
    /// the change — Dataverse customizations still need publishing
    /// separately (confirmed required, unlike form/view import's still-open
    /// question — see `docs/yaml-conventions.md`).
    /// </summary>
    Task UpdateEntityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject entityMetadata, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches one attribute's full, type-specific metadata (via the
    /// type-cast URL, e.g. <c>.../Attributes(LogicalName='x')/Microsoft.Dynamics.CRM.StringAttributeMetadata</c>) —
    /// the object <c>table import</c> mutates in place and PUTs back for an
    /// update, same reasoning as <see cref="GetEntityMetadataJsonAsync"/>.
    /// </summary>
    Task<string> GetAttributeMetadataJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing attribute's full metadata via PUT — see
    /// <see cref="UpdateEntityAsync"/> for why this has to be a full-object
    /// round trip via <see cref="GetAttributeMetadataJsonAsync"/> first, not
    /// a freshly built body. Unlike the GET, the PUT URL itself carries no
    /// type-cast segment — confirmed against Microsoft's own documented
    /// example: the type is declared by <paramref name="attributeMetadata"/>'s
    /// own <c>@odata.type</c>, not the URL.
    /// </summary>
    Task UpdateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a brand-new attribute — see <see cref="AttributeMetadataJsonBuilder.BuildCreateBody"/>
    /// for how <paramref name="attributeMetadata"/> gets built. Only ever
    /// called for a type in <see cref="AttributeMetadataJsonBuilder.SupportedTypes"/>.
    /// </summary>
    Task CreateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken);
}

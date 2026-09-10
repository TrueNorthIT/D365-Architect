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
    /// publish the change itself — <c>form import</c> follows this with its
    /// own call to <see cref="PublishEntityAsync"/> before returning, so the
    /// write is visible to end users without a separate manual publish step.
    /// Doesn't check for a concurrent modification either (no ETag/If-Match)
    /// — see `docs/yaml-conventions.md` for what "checking differences" does
    /// and doesn't cover today.
    /// </summary>
    Task UpdateSystemFormXmlAsync(Uri environmentUrl, string accessToken, Guid formId, string formXml, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes every customization on one table — forms, views, ribbons,
    /// and attributes alike — via the <c>PublishXml</c> action. Dataverse's
    /// own documented <c>ParameterXml</c> schema has no finer-grained way to
    /// publish a single <c>systemform</c> on its own: the <c>&lt;entities&gt;</c>
    /// node only ever takes a whole table's logical name (confirmed against
    /// Microsoft's own docs for <c>PublishXmlRequest.ParameterXml</c> — the
    /// one per-record exception is <c>&lt;dashboards&gt;</c>, a different,
    /// dashboard-only node that doesn't apply to an ordinary form), so
    /// publishing "just the form" <c>form import</c> just wrote actually
    /// means publishing its whole owning table.
    /// </summary>
    Task PublishEntityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken);

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
    /// publish the change itself, and unlike <see cref="UpdateSystemFormXmlAsync"/>
    /// (which <c>form import</c> now follows with its own call to
    /// <see cref="PublishEntityAsync"/>), <c>view import</c> doesn't call
    /// that either yet — a still-open gap, not a closed one, for views.
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

    /// <summary>
    /// Fetches one Boolean/Picklist/MultiSelectPicklist/Status attribute's
    /// choice values — the same type-cast URL shape as
    /// <see cref="GetAttributeMetadataJsonAsync"/>, plus
    /// <c>?$expand=OptionSet,GlobalOptionSet</c>, since neither of those
    /// collection-valued navigation properties comes back otherwise (nor
    /// from the bulk <see cref="GetEntityDefinitionJsonAsync"/> query at
    /// all — confirmed against Microsoft's own docs: you can't
    /// <c>$select</c>/<c>$expand</c> them inside that polymorphic
    /// <c>Attributes</c> collection). A local option set comes back with
    /// <c>OptionSet</c> populated and <c>GlobalOptionSet</c> null; a global
    /// one, the reverse.
    /// </summary>
    Task<string> GetAttributeOptionSetJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a brand-new Lookup column by creating the one-to-many
    /// <em>relationship</em> that owns it — confirmed against Microsoft's
    /// own docs: a plain Lookup attribute only ever comes into existence
    /// this way, never via <see cref="CreateAttributeAsync"/>. See
    /// <see cref="AttributeMetadataJsonBuilder.BuildRelationshipCreateBody"/>
    /// for how <paramref name="relationshipMetadata"/> gets built.
    /// </summary>
    Task CreateOneToManyRelationshipAsync(Uri environmentUrl, string accessToken, JsonObject relationshipMetadata, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a brand-new Customer column via the dedicated
    /// <c>CreateCustomerRelationships</c> action — Microsoft's own docs are
    /// explicit that a Customer lookup needs this rather than a plain
    /// attribute POST or a single <see cref="CreateOneToManyRelationshipAsync"/>
    /// call, since it's really a pair of one-to-many relationships (to
    /// <c>account</c> and <c>contact</c>) sharing one attribute. See
    /// <see cref="AttributeMetadataJsonBuilder.BuildCustomerRelationshipCreateBody"/>.
    /// </summary>
    Task CreateCustomerRelationshipsAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Adds one option to an existing local (never global — see
    /// `docs/yaml-conventions.md`) choice column, or Status column's
    /// status-reason set, via the <c>InsertOptionValue</c> action. Never
    /// called for a Status column — that's <see cref="InsertStatusValueAsync"/>
    /// instead, Dataverse's own dedicated action for it.
    /// </summary>
    Task InsertOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>Renames one existing option's label via the <c>UpdateOptionValue</c> action.</summary>
    Task UpdateOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Removes one option via the <c>DeleteOptionValue</c> action. Built for
    /// completeness against Microsoft's documented shape; <c>table import</c>
    /// never calls this itself today — same "never delete automatically"
    /// policy already applied to whole columns (see `docs/yaml-conventions.md`).
    /// </summary>
    Task DeleteOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>Reorders every option on a column at once via the <c>OrderOption</c> action.</summary>
    Task OrderOptionsAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new status-reason option to a Status (<c>statuscode</c>)
    /// column via the <c>InsertStatusValue</c> action — a Status column is
    /// never independently created (every table already has one), so this
    /// is the only way this tool ever adds to its options.
    /// </summary>
    Task InsertStatusValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Renames an existing State (<c>statecode</c>) option's label via the
    /// <c>UpdateStateValue</c> action — the only change this tool ever makes
    /// to a State column; new state values are never added (every table's
    /// state model is fixed at creation).
    /// </summary>
    Task UpdateStateValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a global choice (<c>GlobalOptionSetDefinitions</c>) by its
    /// unique <c>Name</c>, including its own options. Confirmed live, not
    /// guessed, after two failed attempts: <c>GlobalOptionSetDefinitions</c>
    /// is typed as the abstract <c>OptionSetMetadataBase</c> by default, so
    /// (1) plain <c>?$select=Options</c> without a type-cast 400s ("no
    /// property named 'Options'" on the base type) — needs the same
    /// <c>/Microsoft.Dynamics.CRM.OptionSetMetadata</c> type-cast URL
    /// segment <see cref="GetAttributeOptionSetJsonAsync"/> uses; and (2)
    /// even after the type-cast, <c>Options</c> turns out <em>not</em> to be
    /// expandable the way an attribute's own <c>OptionSet</c>/
    /// <c>GlobalOptionSet</c> nav properties are — <c>?$expand=Options</c>
    /// 400s too ("not a navigation property or complex property") — it must
    /// be named in <c>$select</c> instead, alongside every other field this
    /// tool reads (<c>MetadataId</c>, <c>Name</c>, <c>DisplayName</c>,
    /// <c>Description</c>). Unlike a filtered list query, this is a direct
    /// key lookup and so 404s rather than coming back empty when nothing
    /// matches — returns null in that case rather than throwing, the same
    /// as <see cref="TryGetSystemFormByIdAsync"/>.
    /// </summary>
    Task<string?> TryGetGlobalOptionSetJsonAsync(Uri environmentUrl, string accessToken, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches every global choice in the environment, options included —
    /// the shape <see cref="Conversion.GlobalChoiceJsonReader.ReadMany"/>
    /// reads. What <c>choice export</c> uses when no <c>--solution</c> is
    /// given.
    /// </summary>
    Task<string> GetGlobalOptionSetsJsonAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a solution's unique name to the MetadataIds of the Option
    /// Set (global choice) components it contains — i.e. which global
    /// choices that solution actually customizes, as opposed to every
    /// global choice in the environment. Returns null if no solution with
    /// that unique name exists. The
    /// <see cref="TryGetSolutionAttributeMetadataIdsAsync"/> counterpart for
    /// global choices, same <c>solutioncomponents</c> mechanism, just
    /// <c>componenttype</c> 9 (Option Set) instead of 2 (Attribute) —
    /// confirmed live against this environment's own <c>componenttype</c>
    /// global choice (its Options list names value 9 "Option Set"), the
    /// same empirical standard already applied to Attribute/View/SystemForm.
    /// </summary>
    Task<IReadOnlySet<Guid>?> TryGetSolutionOptionSetMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a brand-new global choice — see
    /// <c>GlobalChoiceMetadataJsonBuilder.BuildCreateBody</c> for how
    /// <paramref name="body"/> gets built. Confirmed against Microsoft's own
    /// documented <c>CreateOptionSet</c> example.
    /// </summary>
    Task CreateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an existing global choice's own metadata (<c>DisplayName</c>/
    /// <c>Description</c> — never its <c>Options</c>, same "options are a
    /// separate action" split as <see cref="UpdateAttributeAsync"/>) via a
    /// full-object PUT, identified by <paramref name="metadataId"/> — unlike
    /// <see cref="TryGetGlobalOptionSetJsonAsync"/>'s own by-Name lookup,
    /// Microsoft's own documented <c>UpdateOptionSet</c> operation is only
    /// confirmed by metadata id, not the Name alternate key, so this tool
    /// always fetches first (via <see cref="TryGetGlobalOptionSetJsonAsync"/>,
    /// which returns the id) rather than guessing the alternate key also
    /// works here.
    /// </summary>
    Task UpdateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, Guid metadataId, JsonObject body, CancellationToken cancellationToken);
}

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
    /// display name — the same identity <c>form export</c> itself uses (see
    /// <see cref="Conversion.Models.FormDefinition"/>'s own doc comment on
    /// why <c>formid</c> isn't part of this tool's YAML). Used by
    /// <c>form build-xml</c> to patch onto the live document rather than
    /// building one from scratch — see
    /// <see cref="Conversion.FormXmlWriter"/>. Returns null when no form by
    /// that name exists yet on that table — the expected case for a form
    /// this tool's YAML describes but that hasn't been created in Dataverse
    /// yet.
    /// </summary>
    /// <exception cref="AmbiguousSystemFormException">More than one form on <paramref name="entityLogicalName"/> is named <paramref name="formName"/> — this tool won't guess which one to patch.</exception>
    Task<string?> TryGetSystemFormXmlAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken);
}

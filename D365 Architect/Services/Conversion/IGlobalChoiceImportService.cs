using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Writes a curated list of <see cref="GlobalChoiceDefinition"/>s back into
/// Dataverse, one plan per choice — the list-shaped counterpart to
/// <see cref="ITableImportService"/>'s per-attribute plans, since a
/// `*.choice.yml` file is a list of choices, not one (see
/// <see cref="IGlobalChoiceExportService"/>'s own doc comment for why).
///
/// Creates a brand-new global choice
/// (<see cref="GlobalChoiceImportAction.Create"/>) for any name nothing
/// live matches yet — unlike <see cref="ITableImportService"/> (which never
/// creates the table itself), since a global choice is its own top-level
/// component rather than something scoped under an already-live table.
/// Otherwise the same shape per choice: <c>DisplayName</c>/<c>Description</c>
/// update via a full-object PUT, options via the separate Insert/Update/
/// Order actions <see cref="GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans"/>
/// builds.
///
/// Deliberately has no <c>WouldRemove</c>-style concept the way
/// <see cref="ITableImportService"/> does for a column present live but
/// missing from the local YAML: a table's own columns are a small, bounded
/// set naturally enumerable in full, but the environment's global choices
/// are not — most local `*.choice.yml` files are already a curated subset
/// (solution-scoped, or hand-picked), so flagging every choice the file
/// doesn't happen to mention would be noise, not a useful signal. This
/// service only ever acts on the choices actually named in the input.
/// </summary>
public interface IGlobalChoiceImportService : IImportService<IReadOnlyList<GlobalChoiceDefinition>, GlobalChoicesImportPreview>
{
    /// <summary>
    /// As the four-argument <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>,
    /// but every brand-new choice this apply creates is also added as a
    /// component of <paramref name="solutionUniqueName"/> when given (via
    /// <see cref="Dataverse.IDataverseClient.CreateGlobalOptionSetAsync"/>'s
    /// own <c>solutionUniqueName</c> parameter — see its doc comment for the
    /// gap this closes). Never affects an update to a choice that already
    /// exists. Null (what the four-argument overload passes) preserves the
    /// old behavior: a new choice lands wherever Dataverse's own default
    /// solution context puts it.
    /// </summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, GlobalChoicesImportPreview preview, string? solutionUniqueName, CancellationToken cancellationToken);
}

/// <summary>What (if anything) <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> will do for one global choice.</summary>
public enum GlobalChoiceImportAction
{
    /// <summary>No global choice with this Name exists live yet — will be created.</summary>
    Create,

    /// <summary>Exists live, with a tracked field and/or its options genuinely different — will be updated.</summary>
    Update,

    /// <summary>Exists live with nothing this tool tracks different — nothing to do.</summary>
    Unchanged,

    /// <summary>
    /// The requested create would fail — see <see cref="GlobalChoiceChangeValidator"/>:
    /// an invalid <c>Name</c> (missing customization prefix, or an invalid
    /// character), or no <c>Options</c>/duplicate option <c>Value</c>s. Also
    /// covers <see cref="GlobalChoiceImportService"/>'s own duplicate-Name
    /// check, which needs to compare across every choice in the input at
    /// once: two choices in the same input claiming the same <c>Name</c>.
    /// Caught before ever building a request, not left for Dataverse's own
    /// API error to explain.
    /// </summary>
    Invalid,
}

/// <param name="Name">The choice's unique name.</param>
/// <param name="Action">What will happen — see <see cref="GlobalChoiceImportAction"/>'s own members.</param>
/// <param name="Reason">Set for <see cref="GlobalChoiceImportAction.Invalid"/>, explaining why nothing will happen.</param>
/// <param name="RequestBody">The full JSON body to POST (create) or PUT (update); null for Unchanged/Invalid, or an Update that's only changing options.</param>
/// <param name="OptionChanges">Separate option-value actions (insert/rename/reorder) to run alongside <paramref name="RequestBody"/> — see <see cref="GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans"/>. Only ever set for <see cref="GlobalChoiceImportAction.Update"/>; null for every other action (a Create's own options are already embedded in its <paramref name="RequestBody"/>).</param>
public sealed record GlobalChoiceImportPlan(string Name, GlobalChoiceImportAction Action, string? Reason, JsonObject? RequestBody, IReadOnlyList<OptionChangePlan>? OptionChanges);

/// <param name="ExistingYaml">What re-exporting just the choices named in the input would produce right now — a choice that doesn't exist live yet is simply omitted, not represented as an empty entry.</param>
/// <param name="NewYaml">The local YAML.</param>
/// <param name="ChoicePlans">One plan per choice named in the input — see <see cref="GlobalChoiceImportAction"/>.</param>
public sealed record GlobalChoicesImportPreview(string ExistingYaml, string NewYaml, IReadOnlyList<GlobalChoiceImportPlan> ChoicePlans)
{
    /// <summary>True when there's at least one actual write to make — a Create or Update plan.</summary>
    public bool HasChanges => ChoicePlans.Any(p => p.Action is GlobalChoiceImportAction.Create or GlobalChoiceImportAction.Update);
}

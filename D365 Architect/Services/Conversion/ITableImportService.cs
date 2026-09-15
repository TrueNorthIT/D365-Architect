using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Writes a curated <see cref="EntityDefinition"/> back into Dataverse —
/// table-level properties (<c>DisplayName</c>/<c>PluralDisplayName</c>/
/// <c>Description</c>) via <see cref="Dataverse.IDataverseClient.UpdateEntityAsync"/>,
/// and columns via <see cref="AttributeImportAction.Create"/>/
/// <see cref="AttributeImportAction.Update"/> plans built per attribute —
/// see <see cref="AttributeMetadataJsonBuilder"/> for exactly which
/// attribute types that covers and why the rest are deliberately excluded.
///
/// Never creates the table itself if it doesn't exist yet, and never
/// deletes a column present live but absent from the local YAML (see
/// <see cref="AttributeImportAction.WouldRemove"/>) — both are the same
/// "this tool doesn't guess at destructive or large-surface operations"
/// discipline <see cref="IFormImportService"/> and
/// <see cref="IViewImportService"/> already apply.
///
/// Every create/update is also checked against
/// <see cref="AttributeChangeValidator"/> before a request is ever built —
/// changing a column's type or SchemaName after creation, an invalid
/// RequiredLevel, or a new column's SchemaName missing a customization
/// prefix all come back as <see cref="AttributeImportAction.Invalid"/>
/// rather than being attempted and left to Dataverse's own API error to
/// explain.
///
/// <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> applies every
/// <see cref="AttributeImportAction.Create"/>/<see cref="AttributeImportAction.Update"/>
/// plan in the preview, plus the table-level update if
/// <see cref="TableImportPreview.TableUpdateBody"/> is set. It doesn't
/// publish the change — Dataverse customizations still need publishing
/// separately (see `docs/yaml-conventions.md`). Defaults to sending every
/// one of those writes as a single atomic <see cref="Dataverse.IDataverseClient.ExecuteTransactionAsync"/>
/// changeset — see the <see cref="ApplyAsync(Uri, string, TableImportPreview, bool, CancellationToken)"/>
/// overload to opt out (<c>table import --no-transaction</c>) and send them
/// one at a time instead, same as before that existed.
/// </summary>
public interface ITableImportService : IImportService<EntityDefinition, TableImportPreview>
{
    /// <param name="environmentUrl">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>.</param>
    /// <param name="accessToken">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>.</param>
    /// <param name="preview">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>.</param>
    /// <param name="useTransaction">
    /// True (the default the four-argument <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>
    /// uses) to send every write in <paramref name="preview"/> as one
    /// atomic Dataverse changeset; false to send them one at a time, same as
    /// this tool always did before batching existed — see
    /// <c>table import --no-transaction</c>.
    /// </param>
    /// <param name="cancellationToken">See <see cref="IImportService{TInput,TPreview}.ApplyAsync"/>.</param>
    Task ApplyAsync(Uri environmentUrl, string accessToken, TableImportPreview preview, bool useTransaction, CancellationToken cancellationToken);
}

/// <summary>What (if anything) <see cref="IImportService{TInput,TPreview}.ApplyAsync"/> will do for one column.</summary>
public enum AttributeImportAction
{
    /// <summary>Present in the local YAML, not live yet — will be created.</summary>
    Create,

    /// <summary>Present on both sides with a tracked field genuinely different — will be updated.</summary>
    Update,

    /// <summary>Present on both sides with nothing this tool tracks different — nothing to do.</summary>
    Unchanged,

    /// <summary>
    /// A difference was found (or the column is new), but its type isn't one
    /// <see cref="AttributeMetadataJsonBuilder.SupportedTypes"/>/<see cref="AttributeMetadataJsonBuilder.CreatableTypes"/>
    /// covers — shown for visibility, never applied.
    /// </summary>
    SkippedUnsupportedType,

    /// <summary>
    /// A brand-new, single-target Lookup column — created by creating the
    /// one-to-many relationship that owns it (see
    /// <see cref="AttributeMetadataJsonBuilder.BuildRelationshipCreateBody"/>/
    /// <see cref="Dataverse.IDataverseClient.CreateOneToManyRelationshipAsync"/>),
    /// never a plain attribute POST.
    /// </summary>
    CreateLookupRelationship,

    /// <summary>
    /// A brand-new Customer column — created via the dedicated
    /// <c>CreateCustomerRelationships</c> action (see
    /// <see cref="AttributeMetadataJsonBuilder.BuildCustomerRelationshipCreateBody"/>/
    /// <see cref="Dataverse.IDataverseClient.CreateCustomerRelationshipsAsync"/>).
    /// </summary>
    CreateCustomerRelationship,

    /// <summary>
    /// Live but absent from the local YAML. Never applied — this tool never
    /// deletes a column automatically, no matter what the YAML says or
    /// doesn't say.
    /// </summary>
    WouldRemove,

    /// <summary>
    /// The requested create/update would fail (or, worse, silently corrupt
    /// something) — see <see cref="AttributeChangeValidator"/> (and
    /// <see cref="Conversion.TableImportService"/>'s own duplicate-SchemaName
    /// check, which needs to compare across every new attribute at once) for
    /// exactly what's checked: changing a column's type or SchemaName after
    /// creation, an invalid RequiredLevel, a new column's SchemaName missing
    /// a customization prefix or containing an invalid character, a Name
    /// that won't match the logical name Dataverse actually derives, two new
    /// columns claiming the same SchemaName, an out-of-range MaxLength/
    /// MinValue/MaxValue/Precision, or MinValue greater than MaxValue.
    /// Caught before ever building a request, not left for Dataverse's own
    /// API error to explain.
    /// </summary>
    Invalid,
}

/// <param name="LogicalName">The column's logical name.</param>
/// <param name="Action">What will happen — see <see cref="AttributeImportAction"/>'s own members.</param>
/// <param name="Reason">Set for <see cref="AttributeImportAction.SkippedUnsupportedType"/>/<see cref="AttributeImportAction.WouldRemove"/>/<see cref="AttributeImportAction.Invalid"/>, explaining why nothing will happen.</param>
/// <param name="RequestBody">The full JSON body to POST (create/relationship-create) or PUT (update); null for every other action.</param>
/// <param name="Warnings">
/// Non-blocking cautions for an <see cref="AttributeImportAction.Update"/>
/// that Dataverse allows but warns against (e.g. lowering MaxLength below
/// what existing data might exceed, or a local option/status value with no
/// live match this tool won't insert automatically) — see
/// <see cref="AttributeChangeValidator.Warnings"/>. Null for every other
/// action.
/// </param>
/// <param name="OptionChanges">
/// Separate option-value actions (insert/rename/reorder) to run alongside
/// this plan's own <paramref name="RequestBody"/> — see
/// <see cref="AttributeMetadataJsonBuilder.BuildOptionChangePlans"/>. Only
/// ever set for an <see cref="AttributeImportAction.Update"/> on an
/// option-bearing type (Boolean/Picklist/MultiSelectPicklist/State/Status);
/// null for every other action.
/// </param>
public sealed record AttributeImportPlan(string LogicalName, AttributeImportAction Action, string? Reason, JsonObject? RequestBody, IReadOnlyList<string>? Warnings = null, IReadOnlyList<OptionChangePlan>? OptionChanges = null);

/// <summary>What kind of option-value action <see cref="OptionChangePlan"/> carries — see <see cref="AttributeMetadataJsonBuilder.BuildOptionChangePlans"/>.</summary>
public enum OptionChangeAction
{
    /// <summary>A brand-new option on an existing local Picklist/MultiSelectPicklist column — <c>InsertOptionValue</c>.</summary>
    InsertOption,

    /// <summary>An existing option's label changed — <c>UpdateOptionValue</c> (also used for Boolean's fixed True/False options, and for Status).</summary>
    UpdateOption,

    /// <summary>The same set of options, reordered — <c>OrderOption</c>.</summary>
    OrderOptions,

    /// <summary>An existing State option's label changed — <c>UpdateStateValue</c>.</summary>
    UpdateStateValue,

    /// <summary>A brand-new status reason on an existing Status column — <c>InsertStatusValue</c>. Dataverse assigns its Value itself; never used for anything else.</summary>
    InsertStatusValue,
}

/// <param name="Action">Which action to call — see <see cref="OptionChangeAction"/>'s own members.</param>
/// <param name="RequestBody">The full JSON body to POST to that action.</param>
/// <param name="Description">A short, human-readable summary for the column plan printout, e.g. "insert option 'Red' (727000000)".</param>
public sealed record OptionChangePlan(OptionChangeAction Action, JsonObject RequestBody, string Description);

/// <param name="EntityLogicalName">The table this preview is for.</param>
/// <param name="ExistingYaml">What re-exporting the table right now would produce.</param>
/// <param name="NewYaml">The local YAML.</param>
/// <param name="TableUpdateBody">
/// The table's own full metadata (fetched via <see cref="Dataverse.IDataverseClient.GetEntityMetadataJsonAsync"/>),
/// with <c>DisplayName</c>/<c>DisplayCollectionName</c>/<c>Description</c>
/// mutated in place — ready to PUT. Null when none of those three actually
/// differ from what's live.
/// </param>
/// <param name="AttributePlans">One plan per column seen on either side — see <see cref="AttributeImportAction"/>.</param>
public sealed record TableImportPreview(string EntityLogicalName, string ExistingYaml, string NewYaml, JsonObject? TableUpdateBody, IReadOnlyList<AttributeImportPlan> AttributePlans)
{
    /// <summary>True when there's at least one actual write to make — a table-level change, or a column plan that writes something (Create/Update/CreateLookupRelationship/CreateCustomerRelationship).</summary>
    public bool HasChanges => TableUpdateBody is not null || AttributePlans.Any(p =>
        p.Action is AttributeImportAction.Create or AttributeImportAction.Update or AttributeImportAction.CreateLookupRelationship or AttributeImportAction.CreateCustomerRelationship);
}

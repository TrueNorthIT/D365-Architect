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
/// </summary>
public interface ITableImportService
{
    Task<TableImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, EntityDefinition entity, CancellationToken cancellationToken);

    /// <summary>
    /// Applies every <see cref="AttributeImportAction.Create"/>/
    /// <see cref="AttributeImportAction.Update"/> plan in
    /// <paramref name="preview"/>, plus the table-level update if
    /// <see cref="TableImportPreview.TableUpdateBody"/> is set. Doesn't
    /// publish the change — Dataverse customizations still need publishing
    /// separately (see `docs/yaml-conventions.md`).
    /// </summary>
    Task ApplyAsync(Uri environmentUrl, string accessToken, TableImportPreview preview, CancellationToken cancellationToken);
}

/// <summary>What (if anything) <see cref="ITableImportService.ApplyAsync"/> will do for one column.</summary>
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
    /// <see cref="AttributeMetadataJsonBuilder.SupportedTypes"/> covers —
    /// shown for visibility, never applied.
    /// </summary>
    SkippedUnsupportedType,

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
/// <param name="RequestBody">The full JSON body to POST (create) or PUT (update); null for every other action.</param>
/// <param name="Warnings">
/// Non-blocking cautions for an <see cref="AttributeImportAction.Update"/>
/// that Dataverse allows but warns against (e.g. lowering MaxLength below
/// what existing data might exceed) — see <see cref="AttributeChangeValidator.Warnings"/>.
/// Null for every other action.
/// </param>
public sealed record AttributeImportPlan(string LogicalName, AttributeImportAction Action, string? Reason, JsonObject? RequestBody, IReadOnlyList<string>? Warnings = null);

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
    /// <summary>True when there's at least one actual write to make — a table-level change, or a Create/Update column plan.</summary>
    public bool HasChanges => TableUpdateBody is not null || AttributePlans.Any(p => p.Action is AttributeImportAction.Create or AttributeImportAction.Update);
}

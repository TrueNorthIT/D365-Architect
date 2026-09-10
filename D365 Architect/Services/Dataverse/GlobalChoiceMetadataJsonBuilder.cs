using System.Text.Json.Nodes;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Builds/mutates the JSON bodies Dataverse's Web API needs to create or
/// update a global choice — the <c>GlobalChoiceDefinition</c> counterpart to
/// <see cref="AttributeMetadataJsonBuilder"/>, confirmed against Microsoft's
/// own documented <c>CreateOptionSet</c>/<c>UpdateOptionSet</c> examples.
///
/// Same split as a column's own options: a choice's <c>DisplayName</c>/
/// <c>Description</c> update via an ordinary full-object PUT
/// (<see cref="ApplyUpdateFields"/>), but its <c>Options</c> never do —
/// Dataverse's own docs are explicit that <c>UpdateOptionSet</c> "only
/// [covers] those properties defined by OptionSetMetadataBase... these
/// properties don't include the options" — so option changes always go
/// through <see cref="BuildOptionChangePlans"/> instead, the same
/// Insert/Update/Order actions <see cref="AttributeMetadataJsonBuilder.BuildOptionChangePlans"/>
/// uses for a local Picklist column, just addressed by <c>OptionSetName</c>
/// instead of <c>AttributeLogicalName</c>/<c>EntityLogicalName</c> (see
/// `docs/yaml-conventions.md`'s own confirmed parameter tables for both).
/// </summary>
internal static class GlobalChoiceMetadataJsonBuilder
{
    /// <summary>
    /// Builds a brand-new global choice's create body — only ever called
    /// for a choice that doesn't exist live yet. Always
    /// <c>OptionSetType: "Picklist"</c> — see <see cref="Conversion.Models.GlobalChoiceDefinition"/>'s
    /// own doc comment for why that's never something the YAML chooses.
    /// </summary>
    public static JsonObject BuildCreateBody(GlobalChoiceDefinition choice)
    {
        var body = new JsonObject
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadata",
            ["Name"] = choice.Name,
            ["OptionSetType"] = "Picklist",
            ["Options"] = new JsonArray(choice.Options!.Select(o => (JsonNode)new JsonObject
            {
                ["Value"] = o.Value,
                ["Label"] = DataverseLabelJson.Build(o.Label),
            }).ToArray()),
        };

        if (choice.DisplayName is not null)
        {
            body["DisplayName"] = DataverseLabelJson.Build(choice.DisplayName);
        }

        if (choice.Description is not null)
        {
            body["Description"] = DataverseLabelJson.Build(choice.Description);
        }

        return body;
    }

    /// <summary>
    /// Mutates <paramref name="existing"/> (the choice's full, live JSON
    /// representation, fetched immediately beforehand so nothing this tool
    /// doesn't understand gets lost) in place, setting only
    /// <see cref="GlobalChoiceDefinition.DisplayName"/>/<see cref="GlobalChoiceDefinition.Description"/>
    /// when <paramref name="local"/> actually specifies them. Never *writes*
    /// <c>Options</c> — see this class's own top-level doc comment — but also
    /// *strips* it from <paramref name="existing"/> if present, rather than
    /// just leaving it untouched: <paramref name="existing"/> comes from
    /// <see cref="Dataverse.IDataverseClient.TryGetGlobalOptionSetJsonAsync"/>,
    /// whose <c>$select</c> deliberately includes <c>Options</c> for
    /// <see cref="BuildOptionChangePlans"/>'s own diffing, so it's still
    /// sitting on the cloned object by the time this runs. Confirmed live:
    /// PUTting it back via <see cref="Dataverse.IDataverseClient.UpdateGlobalOptionSetAsync"/>
    /// — a non-type-cast URL, i.e. the base <c>OptionSetMetadataBase</c> type
    /// — 400s with "Invalid property 'Options' was found in entity
    /// 'Microsoft.Dynamics.CRM.OptionSetMetadataBase'", since that base type
    /// doesn't accept it at all.
    ///
    /// Also sets <c>@odata.type</c> explicitly, for the same
    /// GET-URL-context-doesn't-carry-over-to-PUT reason: <paramref name="existing"/>
    /// was fetched via the type-cast <c>.../Microsoft.Dynamics.CRM.OptionSetMetadata</c>
    /// URL segment, but that segment only tells Dataverse how to *read* the
    /// response — the JSON body itself never carries its own <c>@odata.type</c>
    /// back. Without setting it explicitly here, PUTting the body as-is to
    /// the non-type-cast <c>UpdateGlobalOptionSetAsync</c> URL leaves
    /// Dataverse to infer the type from the URL alone, which resolves to the
    /// abstract <c>OptionSetMetadataBase</c> and 500s with "Cannot create an
    /// abstract class" — confirmed live.
    /// </summary>
    public static void ApplyUpdateFields(JsonObject existing, GlobalChoiceDefinition local)
    {
        existing.Remove("Options");
        existing["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadata";

        if (local.DisplayName is not null)
        {
            existing["DisplayName"] = DataverseLabelJson.Build(local.DisplayName);
        }

        if (local.Description is not null)
        {
            existing["Description"] = DataverseLabelJson.Build(local.Description);
        }
    }

    /// <summary>
    /// Diffs a global choice's local vs. existing options into the
    /// action-based requests needed to reconcile them, via the same
    /// match-by-Value algorithm <see cref="AttributeMetadataJsonBuilder.BuildOptionChangePlans"/>
    /// uses for a column's own local options (see <see cref="OptionSetDiffer"/>)
    /// — an existing option missing from the local YAML is never deleted
    /// automatically, same policy either way.
    /// </summary>
    public static IReadOnlyList<OptionChangePlan> BuildOptionChangePlans(GlobalChoiceDefinition local, GlobalChoiceDefinition existing)
    {
        if (local.Options is null)
        {
            return [];
        }

        return OptionSetDiffer.Diff(local.Options, existing.Options,
            option => BuildInsertOptionPlan(local.Name, option),
            (value, label) => BuildUpdateOptionPlan(local.Name, value, label),
            values => BuildOrderOptionsPlan(local.Name, values));
    }

    private static OptionChangePlan BuildInsertOptionPlan(string optionSetName, AttributeOptionDefinition option) =>
        new(OptionChangeAction.InsertOption, new JsonObject
        {
            ["OptionSetName"] = optionSetName,
            ["Value"] = option.Value,
            ["Label"] = DataverseLabelJson.Build(option.Label),
        }, $"insert option '{option.Label}' ({option.Value})");

    private static OptionChangePlan BuildUpdateOptionPlan(string optionSetName, int value, string label) =>
        new(OptionChangeAction.UpdateOption, new JsonObject
        {
            ["OptionSetName"] = optionSetName,
            ["Value"] = value,
            ["Label"] = DataverseLabelJson.Build(label),
            ["MergeLabels"] = true,
        }, $"rename option {value} to '{label}'");

    private static OptionChangePlan BuildOrderOptionsPlan(string optionSetName, IReadOnlyList<int> values) =>
        new(OptionChangeAction.OrderOptions, new JsonObject
        {
            ["OptionSetName"] = optionSetName,
            ["Values"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray()),
        }, "reorder options");
}

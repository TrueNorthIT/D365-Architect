using System.Text.Json.Nodes;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Builds Dataverse's "Label" complex-type shape for a display-text field
/// (an entity's <c>DisplayName</c>/<c>DisplayCollectionName</c>/<c>Description</c>,
/// or an attribute's <c>DisplayName</c>/<c>Description</c>) — confirmed
/// against Microsoft's own documented create/update examples, not guessed:
/// <c>{ "@odata.type": "Microsoft.Dynamics.CRM.Label", "LocalizedLabels": [{ "@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel", "Label": "...", "LanguageCode": 1033 }] }</c>.
/// Always a single English (1033) label: this tool only ever captures one
/// label per field to begin with (see
/// <see cref="Conversion.EntityJsonDefinitionReader"/>'s own
/// <c>GetLabel</c>), so writing back the same single-language shape is the
/// honest counterpart of that, not a simplification that loses anything
/// this tool already had.
/// </summary>
internal static class DataverseLabelJson
{
    public static JsonObject Build(string text) => new()
    {
        ["@odata.type"] = "Microsoft.Dynamics.CRM.Label",
        ["LocalizedLabels"] = new JsonArray
        {
            new JsonObject
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.LocalizedLabel",
                ["Label"] = text,
                ["LanguageCode"] = 1033,
            },
        },
    };
}

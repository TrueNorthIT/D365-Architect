using System.Text.Json.Nodes;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Builds the JSON body for an option this tool <em>creates</em> (either as
/// part of a create body's <c>Options</c> array or as an
/// <c>InsertOption</c>/<c>InsertOptionValue</c>-style request) — shaped to
/// match what the maker portal writes, so exported solution XML from this
/// tool is indistinguishable from maker-portal options:
/// <c>{ "Value": ..., "Label": ..., "Description": {"@odata.type": "Microsoft.Dynamics.CRM.Label", "LocalizedLabels": [{"@odata.type": "Microsoft.Dynamics.CRM.LocalizedLabel", "Label": "", "LanguageCode": 1033}]}, "ExternalValue": "" }</c>.
/// Creation only — updates and reorders of existing options keep their
/// current (label-only) bodies.
/// </summary>
internal static class DataverseOptionJson
{
    public static JsonObject BuildNewOption(int value, string label) => new()
    {
        ["Value"] = value,
        ["Label"] = DataverseLabelJson.Build(label),
        ["Description"] = DataverseLabelJson.Build(string.Empty),
        ["ExternalValue"] = string.Empty,
    };
}

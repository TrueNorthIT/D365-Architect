namespace D365Architect.Services.Conversion.Models.ControlDefaults;

/// <summary>
/// Confirmed defaults for `MscrmControls.ModelForm.ModelFormControl` (the
/// quick-create control on a lookup). Dataverse's write path needs these
/// present (issue #36: the six Enum parameters missing their text were
/// rejected with 0x80160028, and a `value` block missing its empty/`false`
/// children imported cleanly but 502'd `GetClientMetadata`).
/// </summary>
internal static class ModelFormControlDefaults
{
    private static readonly IReadOnlyDictionary<string, string> EnumAttributes =
        new Dictionary<string, string> { ["type"] = "Enum", ["static"] = "true" };

    private static ParameterDefault Enum(string name, string value) => new(name, value, EnumAttributes);

    public static readonly ControlDefaultsSpec Spec = new()
    {
        ControlName = "MscrmControls.ModelForm.ModelFormControl",
        Evidence = """
            Not documented by Microsoft: the `value` children are the classic Lookup control's
            parameter set (CRM 2016-era FormXml reference); the Enum parameters appear nowhere.
            Surveyed 93 instances across 23 forms from three environments: the nine-element
            `value` block was identical in shape and order on all 93, `SaveMode` was 0 on all 93,
            and the six Enum parameters were `false` on the 69 that have them. The other 24
            (Microsoft's managed msdyn_budget quick-create lookups) predate those six, so
            restoring them on such a form adds them, as the current form designer would.
            """,
        ParameterOrder =
        [
            "value", "QuickForms", "SaveMode", "EnableHighDensityPageHeader", "DisplayFormSelector",
            "AddToRecentItems", "DisplayPopoutCommand", "DisplayNavigateBackCommand", "OverrideXrmPageByFormContext",
        ],
        Parameters =
        [
            Enum("SaveMode", "0"),
            Enum("EnableHighDensityPageHeader", "false"),
            Enum("DisplayFormSelector", "false"),
            Enum("AddToRecentItems", "false"),
            Enum("DisplayPopoutCommand", "false"),
            Enum("DisplayNavigateBackCommand", "false"),
            Enum("OverrideXrmPageByFormContext", "false"),
        ],
        Blocks =
        [
            new BlockDefaults(
                "value",
                ["BindAttribute", "DefaultViewId", "FilterRelationshipName", "DependentAttributeName", "DependentAttributeType",
                 "AvailableViewIds", "AllowFilterOff", "DisableQuickFind", "DisableViewPicker"],
                [
                    new("FilterRelationshipName", ""), new("DependentAttributeName", ""), new("DependentAttributeType", ""),
                    new("AvailableViewIds", ""), new("AllowFilterOff", "false"), new("DisableQuickFind", "false"), new("DisableViewPicker", "false"),
                ]),
        ],
    };
}

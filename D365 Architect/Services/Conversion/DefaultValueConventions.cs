namespace D365Architect.Services.Conversion;

/// <summary>
/// Shared rules for when a value equals Dataverse's own default and should
/// therefore be left out of the curated YAML rather than restated on every
/// single table/column (e.g. every column defaults to RequiredLevel "None";
/// a freshly-created table defaults IsActivity/HasActivities/HasNotes to
/// false). Both <see cref="EntityXmlDefinitionReader"/> and
/// <see cref="EntityJsonDefinitionReader"/> apply these, so a value missing
/// from the YAML consistently means "left at its default", never "we don't
/// know" — the same convention <see cref="Models.EntityDefinition"/> and
/// <see cref="Models.AttributeDefinition"/>'s nullable properties already
/// rely on for values a source simply doesn't have.
/// </summary>
internal static class DefaultValueConventions
{
    /// <summary>"None" is Dataverse's default RequiredLevel for every column type unless set otherwise.</summary>
    public static string? RequiredLevelOrNull(string? value) =>
        string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ? null : value;

    /// <summary>False is the platform default for flags like IsActivity/HasNotes/IsCustomAttribute; only "on" is worth stating.</summary>
    public static bool? TrueOrNull(bool? value) => value == true ? true : null;

    /// <summary>
    /// True is the overwhelmingly common case for flags like IsCustomizable
    /// — everything is customizable unless a managed solution's publisher
    /// deliberately locked it down — so only the noteworthy "off" state is
    /// worth stating. (Confirmed against real exports, not assumed: every
    /// unlocked view/form comes back true, and the exception genuinely
    /// shows up as false on a handful of internal system views.)
    /// </summary>
    public static bool? FalseOrNull(bool? value) => value == false ? false : null;
}

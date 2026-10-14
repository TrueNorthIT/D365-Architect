namespace D365Architect.Services.Conversion.Models.ControlDefaults;

/// <summary>
/// Every control with confirmed defaults. Adding a control is two steps:
/// write its <see cref="ControlDefaultsSpec"/> (one file in this folder, see
/// <see cref="ModelFormControlDefaults"/>) and list it in <see cref="All"/>.
/// The reader and writer look controls up here by name and need no changes.
/// A control that is not listed keeps all its parameters verbatim.
/// </summary>
internal static class ControlDefaultsRegistry
{
    public static IReadOnlyList<ControlDefaultsSpec> All { get; } =
    [
        ModelFormControlDefaults.Spec,
    ];

    public static ControlDefaultsSpec? Find(string? controlName) =>
        controlName is null ? null : All.FirstOrDefault(spec => spec.ControlName == controlName);
}

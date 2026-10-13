namespace D365Architect.Services.Conversion.Models.ControlDefaults;

/// <summary>
/// One parameter a control always carries with the same value: an element
/// named <see cref="Name"/> whose text is <see cref="Value"/>, and — for a
/// typed parameter such as `type="Enum" static="true"` — exactly these
/// <see cref="Attributes"/>.
/// </summary>
public sealed record ParameterDefault(string Name, string Value, IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>
/// A nested element (e.g. `value`) whose own children have confirmed
/// defaults. <see cref="ChildOrder"/> is the order Dataverse itself writes
/// them in; <see cref="Defaults"/> are the children that may be omitted.
/// Children not listed in <see cref="ChildOrder"/> are kept after the
/// known ones.
/// </summary>
public sealed record BlockDefaults(string Name, IReadOnlyList<string> ChildOrder, IReadOnlyList<ParameterDefault> Defaults);

/// <summary>
/// Declarative description of the values a custom (PCF) control's
/// `parameters` always carry, so the curated YAML can leave them out and
/// the FormXML build can put them back. Data only — the logic lives in
/// <see cref="ControlDefaultsApplier"/>, and a spec only takes effect once
/// it is listed in <see cref="ControlDefaultsRegistry"/>.
///
/// A spec must be backed by evidence, never guessed: <see cref="Evidence"/>
/// records what was surveyed (how many instances, from where) and what was
/// or wasn't documented, so the next person can judge — and repeat — it.
/// </summary>
public sealed class ControlDefaultsSpec
{
    /// <summary>The control's fully-qualified name, as in `customControl name="..."`.</summary>
    public required string ControlName { get; init; }

    /// <summary>What the defaults are based on: the survey (counts, sources), what Microsoft documents, and any known exceptions.</summary>
    public required string Evidence { get; init; }

    /// <summary>Top-level parameters that may be omitted when equal to their default.</summary>
    public IReadOnlyList<ParameterDefault> Parameters { get; init; } = [];

    /// <summary>Nested elements whose children have defaults.</summary>
    public IReadOnlyList<BlockDefaults> Blocks { get; init; } = [];

    /// <summary>The order Dataverse writes top-level parameters in; unlisted ones follow.</summary>
    public IReadOnlyList<string> ParameterOrder { get; init; } = [];
}

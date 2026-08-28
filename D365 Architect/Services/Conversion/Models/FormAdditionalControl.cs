using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// An alternate/additional control attached to a <see cref="FormControl"/>
/// via FormXML's separate `&lt;controlDescriptions&gt;` section — e.g. a
/// Calendar control added to a subgrid, or per-client (Web/Phone/Tablet)
/// replacements for a field's usual control ("add a component" in the form
/// designer). This lives entirely outside the `&lt;cell&gt;`/`&lt;control&gt;`
/// tree everything else in this model comes from, cross-referenced back to
/// the base <see cref="FormControl"/> by its `uniqueid` attribute — not the
/// cell's own id or the control's own id, confirmed against a real tenant
/// rather than assumed from the schema alone. Only present on a
/// <see cref="FormControl"/> whose FormXML actually had a `uniqueid` and at
/// least one matching `&lt;controlDescription&gt;`.
/// </summary>
public sealed class FormAdditionalControl
{
    /// <summary>
    /// This alternate's own class id, when it's identified that way (a
    /// GUID) — mutually exclusive with <see cref="Name"/> in every sample
    /// seen so far. Kept raw for the same reason as
    /// <see cref="FormControl.ClassId"/>.
    /// </summary>
    [YamlMember(Order = 0)]
    public string? Id { get; init; }

    /// <summary>
    /// This alternate's fully-qualified PCF control name, e.g.
    /// "MscrmControls.FieldControls.RecentRecords" — mutually exclusive
    /// with <see cref="Id"/> in every sample seen so far.
    /// </summary>
    [YamlMember(Order = 1)]
    public string? Name { get; init; }

    /// <summary>
    /// Which client this alternate applies to (Web/Phone/Tablet), as
    /// Dataverse's raw integer. Not mapped to a friendly name: no
    /// authoritative option set documenting the values was found, same
    /// caution as <see cref="FormControl.ClassId"/>.
    /// </summary>
    [YamlMember(Order = 2)]
    public int? FormFactor { get; init; }

    [YamlMember(Order = 3)]
    public string? Version { get; init; }

    /// <summary>This alternate's own parameters, converted the same structural way as <see cref="FormControl.Parameters"/> (including the same false-stripping rule).</summary>
    [YamlMember(Order = 4)]
    public object? Parameters { get; init; }
}

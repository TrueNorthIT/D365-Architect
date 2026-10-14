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
    /// This alternate's own class id (a GUID), when it's identified that
    /// way — mutually exclusive with name.
    /// </summary>
    [YamlMember(Order = 0)]
    public string? Id { get; init; }

    /// <summary>
    /// This alternate's fully-qualified PCF control name, e.g.
    /// "MscrmControls.FieldControls.RecentRecords" — mutually exclusive
    /// with id.
    /// </summary>
    [YamlMember(Order = 1)]
    public string? Name { get; init; }

    /// <summary>
    /// Which client this alternate applies to (Web/Phone/Tablet), as
    /// Dataverse's raw integer — not mapped to a friendly name, since no
    /// authoritative reference for the values was found.
    /// </summary>
    [YamlMember(Order = 2)]
    public int? FormFactor { get; init; }

    /// <summary>The alternate control's version.</summary>
    [YamlMember(Order = 3)]
    public string? Version { get; init; }

    /// <summary>This alternate's own settings, same shape as a control's own <c>parameters</c>.</summary>
    [YamlMember(Order = 4)]
    public object? Parameters { get; init; }
}

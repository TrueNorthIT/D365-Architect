using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// A field the form tracks but never renders on any tab — FormXML's
/// `&lt;hiddencontrols&gt;/&lt;data&gt;` (e.g. the composite address fields a
/// standard "Address" control depends on). Distinct from a
/// <see cref="FormControl"/> with <see cref="FormControl.Visible"/> false:
/// that one still occupies a cell somewhere on the form, just hidden; this
/// one has no cell at all.
/// </summary>
public sealed class FormHiddenField
{
    /// <summary>The attribute logical name, e.g. "address1_addressid" (FormXML's `datafieldname`).</summary>
    [YamlMember(Order = 0)]
    public required string Field { get; init; }

    /// <summary>The field's raw control class id, if Dataverse records one for it.</summary>
    [YamlMember(Order = 1)]
    public string? ClassId { get; init; }

    /// <summary>
    /// Ties this hidden field to a specific relationship, if Dataverse
    /// records one for it — FormXML's `relationship` attribute, alongside
    /// its sibling <see cref="ClassId"/>/<see cref="Field"/> on the same
    /// `&lt;data&gt;` element. Absent when this hidden field's FormXML
    /// doesn't set it.
    /// </summary>
    [YamlMember(Order = 2)]
    public string? Relationship { get; init; }
}

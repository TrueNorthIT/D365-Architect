using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// Controls when this form is offered as a choice for a record — FormXML's
/// `&lt;DisplayConditions&gt;`. Common on any table with more than one Main
/// form: this is how an app knows which one to fall back to, and which
/// security roles see this one as an option at all.
/// </summary>
public sealed class FormDisplayCondition
{
    /// <summary>Whether this form is the fallback when no other form's conditions match. Only present when true — most forms with display conditions aren't the fallback.</summary>
    [YamlMember(Order = 0)]
    public bool? FallbackForm { get; init; }

    /// <summary>Where this form ranks against a table's other forms when a user is offered a choice.</summary>
    [YamlMember(Order = 1)]
    public int? Order { get; init; }

    /// <summary>
    /// Security role ids this form is restricted to. Absent means every
    /// role can see it (FormXML's `&lt;Everyone /&gt;` — the common case);
    /// present means only these roles can (FormXML's `&lt;Role Id="..."/&gt;`
    /// list) — the two are mutually exclusive on a real form.
    /// </summary>
    [YamlMember(Order = 2)]
    public IReadOnlyList<string>? Roles { get; init; }
}

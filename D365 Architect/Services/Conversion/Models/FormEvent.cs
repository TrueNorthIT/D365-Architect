using YamlDotNet.Serialization;

namespace D365Architect.Services.Conversion.Models;

/// <summary>
/// A JavaScript event binding on this form — FormXML's `&lt;event&gt;`. Business
/// logic (what runs on load/save/a field changing), not layout — genuinely
/// significant for reviewing or changing a form's behaviour, distinct from
/// everything else in <see cref="FormDefinition"/> describing what's shown.
/// </summary>
public sealed class FormEvent
{
    /// <summary>The event's own name, e.g. "onload", or a custom name for a field-level event tied to attribute.</summary>
    [YamlMember(Order = 0)]
    public string? Name { get; init; }

    /// <summary>The attribute logical name this event fires for, when it's field-level (a "field changed" event) rather than form-level.</summary>
    [YamlMember(Order = 1)]
    public string? Attribute { get; init; }

    /// <summary>Whether this event is active.</summary>
    [YamlMember(Order = 2)]
    public bool? Active { get; init; }

    /// <summary>Handlers configured through the form designer's own "Event Handlers" UI. Absent when there are none.</summary>
    [YamlMember(Order = 3)]
    public IReadOnlyList<FormEventHandler>? Handlers { get; init; }

    /// <summary>Handlers the platform itself injects (e.g. for a related-field default) rather than ones a maker configured. Absent when there are none.</summary>
    [YamlMember(Order = 4)]
    public IReadOnlyList<FormEventHandler>? InternalHandlers { get; init; }
}

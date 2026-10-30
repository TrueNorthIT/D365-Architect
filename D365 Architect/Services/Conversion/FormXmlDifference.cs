namespace D365Architect.Services.Conversion;

/// <summary>
/// One difference <see cref="FormXmlComparer"/> found between two
/// <c>&lt;form&gt;</c> elements. <see cref="Path"/> is the
/// <c>/</c>-separated element path from below <c>&lt;form&gt;</c>, each segment keyed
/// (<c>tab[name=X]</c>, <c>section[name=X]</c>, <c>control[id=X]</c>,
/// <c>Library[name=X]</c>, <c>Handler[functionName=X]</c>) or otherwise by its 1-based
/// position among same-named siblings (<c>row[3]</c>); an attribute difference ends in <c>/@attr</c>.
/// For <see cref="FormXmlDifferenceKind.ElementMoved"/>, <see cref="Before"/> and
/// <see cref="After"/> are the element's 1-based position among the siblings both sides share.
/// </summary>
public sealed record FormXmlDifference(string Path, FormXmlDifferenceKind Kind, string? Before, string? After);

public enum FormXmlDifferenceKind
{
    ElementAdded,
    ElementRemoved,
    AttributeAdded,
    AttributeRemoved,
    AttributeChanged,
    TextChanged,
    ElementMoved,
}

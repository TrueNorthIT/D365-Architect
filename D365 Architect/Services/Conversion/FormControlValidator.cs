using System.Xml.Linq;
using System.Xml.Schema;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Catches a control's <c>Control</c>/<c>CustomControlId</c> problems
/// before <c>form import</c> ever writes them:
/// <list type="bullet">
/// <item>Both set at once — mutually exclusive, see
/// <see cref="FormControl.Control"/>'s own doc comment.</item>
/// <item><c>Control</c> naming something not in
/// <see cref="StandardFormControls"/> — almost certainly a typo, since a
/// real standard control name only ever comes from a fresh `form export`
/// in the first place.</item>
/// <item>No resolvable classid at all — confirmed live that Dataverse's
/// write-time validation rejects this outright ("The class id cannot be
/// null for control element..."), even though neither this tool's own
/// model nor Microsoft's official FormXML XSD requires <c>classid</c> at
/// all (so <see cref="FormXmlValidator"/>'s schema check alone would never
/// catch this — exactly the same "the schema is more lenient than
/// Dataverse's real write-time rules" gap the <c>parameters</c>/
/// <c>TypeName</c> incident already exposed, just on a missing attribute
/// this time instead of an extra element). Every real control checked so
/// far (subgrids, web resources, plain field controls) carries a
/// <c>classid</c>, so this treats a missing one as the exception needing
/// justification, not the rule.</item>
/// </list>
///
/// A control whose existing, live counterpart *also* has no resolvable
/// classid is exempted from that last check rather than blocked — the
/// same "don't block on a value nobody's actually trying to change"
/// discipline <see cref="Dataverse.AttributeChangeValidator"/>'s
/// Precision/MaxLength range checks already apply for the identical
/// reason (confirmed live there too, against <c>account</c>'s own
/// <c>exchangerate</c> column): re-submitting an already-classid-less
/// control unchanged shouldn't fail just because some *other* part of the
/// form changed and forced this control to be rewritten too —
/// <c>header</c>/<c>footer</c>/<c>tabs</c> are always replaced wholesale
/// by <see cref="FormXmlWriter"/> rather than patched control-by-control.
/// </summary>
public static class FormControlValidator
{
    /// <returns>One <see cref="FormXmlValidationMessage"/> per control that would fail this way — empty when none would.</returns>
    public static IReadOnlyList<FormXmlValidationMessage> Validate(FormDefinition form, XElement existingRoot)
    {
        var messages = new List<FormXmlValidationMessage>();

        foreach (var control in AllControls(form))
        {
            if (control.Control is not null && control.CustomControlId is not null)
            {
                messages.Add(Message($"Control '{control.Id}' has both 'control' ('{control.Control}') and 'customControlId' ('{control.CustomControlId}') set — mutually exclusive, pick one."));
                continue;
            }

            if (control.Control is not null && !StandardFormControls.IsKnownFriendlyName(control.Control))
            {
                messages.Add(Message($"Control '{control.Id}' has 'control: {control.Control}', which isn't a recognized standard control name — check for a typo (see StandardFormControls for the full list), or use 'customControlId' with the raw class id instead if this genuinely is a custom/PCF control."));
                continue;
            }

            if (StandardFormControls.Resolve(control) is not null || ExistingControlAlsoLacksClassId(existingRoot, control.Id))
            {
                continue;
            }

            var boundTo = control.Field is not null ? $" (bound to '{control.Field}')" : "";
            messages.Add(Message($"Control '{control.Id}'{boundTo} has no 'control' or 'customControlId' set. Dataverse's own FormXML schema doesn't require a classid at all, but its write-time validation does — confirmed live ('The class id cannot be null for control element...') — so this would fail on import rather than being written with no renderer specified."));
        }

        return messages;
    }

    private static FormXmlValidationMessage Message(string text) => new(XmlSeverityType.Error, 0, 0, text, "", 0);

    private static IEnumerable<FormControl> AllControls(FormDefinition form) =>
        form.Tabs
            .SelectMany(t => t.Columns)
            .SelectMany(c => c.Sections)
            .SelectMany(s => s.Controls)
            .Concat(form.HeaderControls ?? [])
            .Concat(form.FooterControls ?? []);

    private static bool ExistingControlAlsoLacksClassId(XElement existingRoot, string controlId)
    {
        var existingControl = existingRoot.Descendants("control").FirstOrDefault(c => (string?)c.Attribute("id") == controlId);
        return existingControl is not null && existingControl.Attribute("classid") is null;
    }
}

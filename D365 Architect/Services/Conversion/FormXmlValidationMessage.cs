using System.Xml.Schema;

namespace D365Architect.Services.Conversion;

/// <summary>
/// One violation from <see cref="FormXmlValidator.Validate"/>.
/// </summary>
/// <param name="Severity">
/// .NET's own <see cref="XmlSeverityType"/> — <c>Error</c> for a genuine
/// content-model/attribute violation, <c>Warning</c> for the small set of
/// lenient cases the schema validator itself treats as non-fatal (e.g. no
/// declaration found under a lax wildcard). Confirmed empirically across
/// every violation shape checked so far (undeclared attribute, invalid
/// child element, incomplete content, invalid choice content): all of them
/// come back <c>Error</c> — this schema doesn't use lax/skip wildcards
/// anywhere this tool's own output reaches, so <c>Warning</c> may never
/// actually appear in practice. Exposed anyway, since it costs nothing and
/// .NET is the authority on it, not this tool. Deliberately NOT what
/// <see cref="IsKnownHarmless"/> is based on — see that property's own doc
/// comment for why this alone was never a safe signal for whether Dataverse
/// will actually reject the write.
/// </param>
/// <param name="LineNumber">1-based line the violation was reported at.</param>
/// <param name="LinePosition">1-based column on that line.</param>
/// <param name="Message">The validator's own message, e.g. "The 'x' attribute is not declared."</param>
/// <param name="Snippet">
/// A short excerpt of the offending FormXML centered on
/// <paramref name="LinePosition"/> — since <see cref="FormXmlWriter"/>
/// always writes FormXML as one long line, a bare line/column pair alone
/// isn't enough to actually find the spot by eye.
/// </param>
/// <param name="SnippetCaretOffset">
/// The index into <paramref name="Snippet"/> of the exact character
/// <paramref name="LinePosition"/> points at — deliberately an offset for
/// the caller to highlight inline (e.g. inverse video around that one
/// character) rather than a second line of spaces-and-a-caret underneath:
/// a console can wrap a long snippet onto more than one display line,
/// which would silently misalign a separate caret line but doesn't affect
/// an inline highlight, since it travels with the character itself.
/// </param>
public sealed record FormXmlValidationMessage(XmlSeverityType Severity, int LineNumber, int LinePosition, string Message, string Snippet, int SnippetCaretOffset)
{
    /// <summary>
    /// True only for the exact, specifically-confirmed-safe violation
    /// patterns:
    /// <list type="bullet">
    /// <item>An undeclared <c>headerdensity</c>/<c>showinformselector</c>
    /// attribute on the form root — real, live Dataverse-produced FormXML
    /// already carries both on every form checked, and re-submitting them
    /// unchanged (this tool never touches either) has been confirmed not to
    /// be rejected.</item>
    /// <item>An invalid <c>UClientActivitiesConfigurationJSON</c>/
    /// <c>UClientNotesConfigurationJSON</c> child element inside the
    /// Timeline control's own <c>&lt;parameters&gt;</c>
    /// (<c>UnifiedClientTimelineWallParameters</c> in the XSD) — the
    /// standard, Microsoft-shipped default per-activity-type JSON config
    /// that control ships with on effectively every entity form with a
    /// timeline, found byte-identical across two independently-exported
    /// real forms. The vendored XSD (downloaded a point in time, see
    /// `Resources/FormXmlSchema/NOTICE.md`) simply doesn't declare either
    /// element for that control — not this tool's own writer inventing or
    /// mangling anything, since both are round-tripped verbatim from what
    /// export captured. Confirmed safe by the user's own direct D365 admin
    /// knowledge of this specific pattern, not (yet) by an actual live
    /// Dataverse write — narrower than that bar the rest of this list holds
    /// to, so treat this one entry with a bit more caution than the others
    /// if it ever needs revisiting.</item>
    /// </list>
    /// Every other violation — including one that looks structurally
    /// similar, e.g. a different undeclared attribute elsewhere, or any
    /// other invalid child element — is <em>not</em> assumed safe by
    /// extension: a genuine "invalid child element" violation (a stray
    /// <c>TypeName</c> inside a control's own <c>&lt;parameters&gt;</c>,
    /// confirmed live) was once waved through on the same "schema vs. real
    /// Dataverse output disagree sometimes" reasoning that only the patterns
    /// above actually earned, and Dataverse's own write-time validation
    /// rejected it outright with a 400. A second, separate incident showed
    /// the same gap the other direction — Dataverse rejecting a control
    /// with no <c>classid</c> ("The class id cannot be null for control
    /// element...") even though the schema never declares it required at
    /// all — see <see cref="FormControlValidator"/>, which produces its own
    /// findings as this exact type specifically so they get the same
    /// treatment. So: this property is deliberately
    /// narrow rather than a general severity signal, and callers that write
    /// to Dataverse (<c>form import</c>) should treat <em>every other</em>
    /// violation as blocking by default — see <see cref="FormXmlValidator"/>'s
    /// own doc comment.
    /// </summary>
    public bool IsKnownHarmless =>
        Message.Contains("'headerdensity'", StringComparison.Ordinal) ||
        Message.Contains("'showinformselector'", StringComparison.Ordinal) ||
        Message.Contains("'UClientActivitiesConfigurationJSON'", StringComparison.Ordinal) ||
        Message.Contains("'UClientNotesConfigurationJSON'", StringComparison.Ordinal);
}

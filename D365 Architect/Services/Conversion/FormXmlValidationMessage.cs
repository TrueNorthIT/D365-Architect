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
/// .NET is the authority on it, not this tool. Either severity is still
/// just a warning as far as <c>form build-xml</c> itself is concerned — see
/// <see cref="FormXmlValidator"/>'s own doc comment for why a violation
/// isn't necessarily this tool's bug.
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
public sealed record FormXmlValidationMessage(XmlSeverityType Severity, int LineNumber, int LinePosition, string Message, string Snippet, int SnippetCaretOffset);

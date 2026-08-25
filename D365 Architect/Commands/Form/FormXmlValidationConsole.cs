using D365Architect.Services.Conversion;
using Spectre.Console;

namespace D365Architect.Commands.Form;

/// <summary>
/// Renders <see cref="FormXmlValidator"/> results — shared by
/// <see cref="BuildFormXmlCommand"/> and <see cref="ImportFormCommand"/>
/// (which each reach <see cref="FormXmlValidator"/> independently; this is
/// just their one shared piece of console-rendering, not shared logic).
/// </summary>
internal static class FormXmlValidationConsole
{
    public static void PrintViolations(IReadOnlyList<FormXmlValidationMessage> violations)
    {
        if (violations.Count == 0)
        {
            return;
        }

        AnsiConsole.MarkupLine($"[yellow]{violations.Count} schema violation(s) against Microsoft's own FormXML schema — see FormXmlValidator's own doc comment for why this isn't necessarily a bug:[/]");
        foreach (var violation in violations)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]  [[{violation.Severity}]] Line {violation.LineNumber}, position {violation.LinePosition}: {violation.Message.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine($"[grey]    {HighlightSnippet(violation.Snippet, violation.SnippetCaretOffset)}[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Renders <paramref name="snippet"/> with the character at
    /// <paramref name="caretOffset"/> highlighted inline (inverse video)
    /// rather than pointed at with a second line of spaces-and-a-caret
    /// underneath — see <see cref="FormXmlValidationMessage.SnippetCaretOffset"/>'s
    /// own doc comment for why: a wrapped console line would silently throw
    /// off a separate caret line's alignment, but an inline highlight stays
    /// correct regardless, since it travels with the character itself.
    /// </summary>
    private static string HighlightSnippet(string snippet, int caretOffset)
    {
        if (caretOffset < 0 || caretOffset >= snippet.Length)
        {
            return snippet.EscapeMarkup();
        }

        var before = snippet[..caretOffset].EscapeMarkup();
        var at = snippet[caretOffset].ToString().EscapeMarkup();
        var after = snippet[(caretOffset + 1)..].EscapeMarkup();
        return $"{before}[invert]{at}[/]{after}";
    }
}

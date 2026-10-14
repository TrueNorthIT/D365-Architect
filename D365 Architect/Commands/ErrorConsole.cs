using Spectre.Console;

namespace D365Architect.Commands;

/// <summary>
/// Prints a red (<see cref="Print(FormattableString)"/>) or yellow
/// (<see cref="Warn(FormattableString)"/>) message to the console —
/// safely, so a raw exception message, file path, or CLI argument can never
/// crash the renderer itself and mask the real error underneath it.
///
/// Every command's catch block used to build this by hand, e.g.
/// <c>AnsiConsole.MarkupLine($"[red]{ex.Message}[/]")</c> — and dozens of
/// call sites across most command files all made the identical mistake:
/// interpolated text this tool doesn't fully control (most notably a
/// Dataverse HTTP error body inside <c>ex.Message</c>, but also a file path
/// or a raw CLI argument, both of which can legally contain <c>[</c>/<c>]</c>
/// on Windows) can carry an unescaped <c>[...]</c> sequence that makes
/// Spectre.Console try to parse it as markup and throw its own unrelated
/// <c>Could not find color or style '...'</c> error — confirmed live, where
/// it hid a real Dataverse rejection behind that crash instead of showing
/// it. Fixed at the time by adding <c>.EscapeMarkup()</c> at each call site
/// by hand (see `docs/DEVELOPING.md`) — safe, but only as safe as every
/// future call site remembering to do the same thing.
///
/// This exists to make that mistake structurally impossible instead: both
/// methods take a plain interpolated string, e.g.
/// <c>ErrorConsole.Print($"Couldn't parse '{path}' as a view: {ex.Message}")</c>,
/// written exactly like any other interpolated string. Because the
/// parameter type is <see cref="FormattableString"/> rather than
/// <see cref="string"/>, the compiler hands this method the format
/// template and its arguments *separately* instead of already
/// concatenating them (a real C# feature — target-typed interpolated
/// strings — not a trick specific to this method), so every argument can
/// be escaped here, once, before it ever reaches Spectre — the literal
/// template text a developer wrote is trusted as-is, exactly like markup
/// written directly in an <c>AnsiConsole.MarkupLine</c> call today; only
/// the *interpolated values* get escaped, which is exactly the content
/// this whole class exists to make safe. A future call site cannot forget
/// this step — there's nothing to remember.
/// </summary>
internal static class ErrorConsole
{
    /// <summary>Prints <paramref name="message"/> in red, escaping every interpolated value.</summary>
    public static void Print(FormattableString message) =>
        AnsiConsole.MarkupLine($"[red]{Format(message)}[/]");

    /// <summary>As <see cref="Print(FormattableString)"/>, for an exception with no other context.</summary>
    public static void Print(Exception ex) => Print($"{ex.Message}");

    /// <summary>As <see cref="Print(FormattableString)"/>, but yellow — for a warning rather than an error. Same escaping, same reason it matters.</summary>
    public static void Warn(FormattableString message) =>
        AnsiConsole.MarkupLine($"[yellow]{Format(message)}[/]");

    private static string Format(FormattableString message)
    {
        var arguments = message.GetArguments();
        var escaped = new object?[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            escaped[i] = arguments[i]?.ToString()?.EscapeMarkup() ?? "";
        }

        return string.Format(message.Format, escaped);
    }
}

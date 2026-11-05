using D365Architect.Commands;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Commands;

/// <summary>
/// Pins ErrorConsole's exception reporting: the <c>Print(Exception)</c>
/// overload prints the full exception (Exception.ToString()), not just the
/// top-level message, so a wrapped failure — e.g. a Dataverse HTTP error
/// inside a broader one — stays diagnosable instead of silently losing its
/// inner chain.
///
/// In the "Console" collection because it swaps <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ErrorConsoleTests
{
    [Fact]
    public void PrintException_KeepsInnerExceptionDetail()
    {
        using var console = new ConsoleHarness();

        ErrorConsole.Print(new InvalidOperationException("outer failed",
            new HttpRequestException("AADSTS7000215 user disabled")));

        Expect.Contains("outer failed", console.Output, "the top-level message should be printed.");
        Expect.Contains("HttpRequestException", console.Output, "the inner exception's type should be printed.");
        Expect.Contains("AADSTS7000215 user disabled", console.Output, "the inner exception's message should be printed.");
        Expect.Contains("--- End of inner exception stack trace ---", console.Output, "the inner-exception chain should be printed, not just the top-level message.");
    }

    // An exception message carrying '[...]' must not make Spectre parse it as markup and crash the renderer —
    // the same reason every ErrorConsole call site escapes its interpolated values.
    [Fact]
    public void PrintException_EscapesMarkupInTheMessage()
    {
        using var console = new ConsoleHarness();

        ErrorConsole.Print(new InvalidOperationException("rejected: [weird] body"));

        Expect.Contains("rejected: [weird] body", console.Output, "the raw message, brackets included, should be printed.");
    }
}

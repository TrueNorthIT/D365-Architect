using Spectre.Console;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Swaps the global <see cref="AnsiConsole.Console"/> for a plain-text,
/// non-interactive one that records everything written. Tests using it must
/// be in the "Console" collection (parallelism disabled), since the console is
/// process-global.
/// </summary>
public static class ConsoleCapture
{
    public static async Task<(T Result, string Output)> RunAsync<T>(Func<Task<T>> action)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 200;

        var previous = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            var result = await action();
            return (result, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }
}

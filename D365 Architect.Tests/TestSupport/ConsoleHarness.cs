using D365Architect.Services.Authentication;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Tests that swap the process-wide <see cref="AnsiConsole.Console"/> must
/// not run alongside each other (or alongside anything else printing), so
/// every such test class joins this collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "Console";
}

/// <summary>
/// Points <see cref="AnsiConsole.Console"/> at an in-memory console for the
/// life of the test, so a command's printed output can be asserted and its
/// confirmation prompt answered (<paramref name="answers"/>, one string per
/// prompt, e.g. <c>"y"</c>). If the command prompts more times than there
/// are answers the test fails immediately rather than hanging - which is
/// how a test proves <c>--yes</c> never prompts. No new package: this is a
/// few lines over Spectre.Console's own public types.
/// </summary>
public sealed class ConsoleHarness : IDisposable
{
    private readonly IAnsiConsole _previous = AnsiConsole.Console;
    private readonly StringWriter _writer = new();

    public ConsoleHarness(params string[] answers)
    {
        var inner = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            Out = new AnsiConsoleOutput(_writer),
        });
        inner.Profile.Width = 240;

        Console = new ScriptedConsole(inner, new ScriptedInput(answers));
        AnsiConsole.Console = Console;
    }

    public IAnsiConsole Console { get; }

    /// <summary>Everything printed so far, as plain text.</summary>
    public string Output => _writer.ToString();

    public void Dispose() => AnsiConsole.Console = _previous;

    private sealed class ScriptedConsole(IAnsiConsole inner, IAnsiConsoleInput input) : IAnsiConsole
    {
        public Profile Profile => inner.Profile;
        public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;
        public IAnsiConsoleInput Input => input;
        public RenderPipeline Pipeline => inner.Pipeline;
        public IAnsiConsoleCursor Cursor => inner.Cursor;
        public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
        public void Clear(bool home) => inner.Clear(home);
        public void Write(IRenderable renderable) => inner.Write(renderable);
    }

    private sealed class ScriptedInput : IAnsiConsoleInput
    {
        private readonly Queue<ConsoleKeyInfo> _keys = new();

        public ScriptedInput(IEnumerable<string> answers)
        {
            foreach (var answer in answers)
            {
                foreach (var c in answer)
                {
                    _keys.Enqueue(new ConsoleKeyInfo(c, (ConsoleKey)char.ToUpperInvariant(c), false, false, false));
                }

                _keys.Enqueue(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            }
        }

        public bool IsKeyAvailable() => _keys.Count > 0;

        public ConsoleKeyInfo? ReadKey(bool intercept) =>
            _keys.Count > 0 ? _keys.Dequeue() : throw new InvalidOperationException("The command asked for confirmation but the test supplied no answer - it should not have prompted here.");

        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
    }
}

/// <summary>Always signed in to one fixed environment - the only thing the import flow needs from authentication.</summary>
public sealed class FakeAuthenticationService : IAuthenticationService
{
    public static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    public Task<AuthenticatedContext> LoginInteractiveAsync(Uri environmentUrl, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<AuthenticatedContext> GetCurrentContextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AuthenticatedContext(EnvironmentUrl, "tester@example.com", "token"));
}

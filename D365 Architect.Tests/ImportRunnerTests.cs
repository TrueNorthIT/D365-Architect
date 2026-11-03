using D365Architect.Commands;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using Spectre.Console;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests;

/// <summary>
/// Covers <see cref="ImportRunner.PreviewAsync{TSettings,TInput,TPreview}"/> directly:
/// what it prints and returns for each safety gate, and in particular that an
/// asset whose only outcome is "nothing applicable" is silent in
/// <c>solution import</c> mode unless verbose. Swaps the global
/// <see cref="AnsiConsole.Console"/> for a recording one, so these must not run in parallel with
/// anything else that writes to it.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportRunnerTests
{
    private sealed class Settings : ImportSettingsBase;

    private sealed class FakeAuth : IAuthenticationService
    {
        public Task<AuthenticatedContext> LoginInteractiveAsync(Uri environmentUrl, CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<AuthenticatedContext> GetCurrentContextAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AuthenticatedContext(new Uri("https://test.crm.dynamics.com"), "user", "token"));
    }

    private sealed class FakeService : IImportService<string, string>
    {
        public Task<string> PreviewAsync(Uri environmentUrl, string accessToken, string input, CancellationToken cancellationToken) => Task.FromResult("preview");

        public Task ApplyAsync(Uri environmentUrl, string accessToken, string preview, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static ImportFlowSpec<Settings, string, string> Spec(
        Func<string, string?>? skipBefore = null,
        Func<string, string?>? skipAfter = null,
        Func<Settings, string, string?>? block = null) => new()
    {
        ReadInputAsync = (_, _) => Task.FromResult<string?>("input"),
        SubjectName = _ => "Subject",
        PreviewStatusMessage = _ => "Looking up...",
        SkipBeforePrinting = skipBefore ?? (_ => null),
        PrintChanges = _ => AnsiConsole.MarkupLine("THE-DIFF"),
        SkipAfterPrinting = skipAfter ?? (_ => null),
        BlockReason = block ?? ((_, _) => null),
        ApplyStatusMessage = "Importing...",
        ApplyAsync = (_, _, _) => Task.CompletedTask,
        PrintSuccess = (_, _) => { },
        FormatDomainException = (_, _) => null,
    };

    private static Task<(PreparedImport Result, string Output)> Preview(ImportFlowSpec<Settings, string, string> spec, bool printUnchanged) =>
        ConsoleCapture.RunAsync(() => new ImportRunner(new FakeAuth()).PreviewAsync(new Settings { Input = "x" }, new FakeService(), spec, printUnchanged, CancellationToken.None));

    [Fact]
    public async Task Changed_PrintsTheDiffAndReturnsAnApply()
    {
        var (result, output) = await Preview(Spec(), printUnchanged: false);

        Assert.Equal(ImportOutcome.Changed, result.Outcome);
        Assert.NotNull(result.ApplyAsync);
        Assert.Contains("THE-DIFF", output);
    }

    [Fact]
    public async Task SkipBeforePrinting_PrintsNoChangesOnlyWhenPrintUnchanged()
    {
        var spec = Spec(skipBefore: _ => "already matches.");

        var (quiet, quietOutput) = await Preview(spec, printUnchanged: false);
        var (loud, loudOutput) = await Preview(spec, printUnchanged: true);

        Assert.Equal(ImportOutcome.Unchanged, quiet.Outcome);
        Assert.False(quiet.PrintedOutput);
        Assert.DoesNotContain("No changes", quietOutput);
        Assert.Equal(ImportOutcome.Unchanged, loud.Outcome);
        Assert.True(loud.PrintedOutput);
        Assert.Contains("No changes", loudOutput);
    }

    [Fact]
    public async Task BlockReason_ReturnsFailedAndSaysWhy()
    {
        var (result, output) = await Preview(Spec(block: (_, _) => "schema violation"), printUnchanged: false);

        Assert.Equal(ImportOutcome.Failed, result.Outcome);
        Assert.Contains("Refusing to import", output);
        Assert.Contains("schema violation", output);
    }

    // Nothing applicable is an unchanged asset; in solution-import mode
    // (printUnchanged false, i.e. not --verbose) it must print nothing at all.
    [Fact]
    public async Task SkipAfterPrinting_WithoutVerbose_IsSilentAndUnchanged()
    {
        var (result, output) = await Preview(Spec(skipAfter: _ => "Nothing above is actually applicable"), printUnchanged: false);

        Assert.Equal(ImportOutcome.Unchanged, result.Outcome);
        Assert.False(result.PrintedOutput);
        Assert.DoesNotContain("Changes for", output);
        Assert.DoesNotContain("THE-DIFF", output);
        Assert.DoesNotContain("Nothing above", output);
    }

    // Single-asset imports (and --verbose) keep the full explanation.
    [Fact]
    public async Task SkipAfterPrinting_WithPrintUnchanged_PrintsTheDiffAndMessage()
    {
        var (result, output) = await Preview(Spec(skipAfter: _ => "Nothing above is actually applicable"), printUnchanged: true);

        Assert.Equal(ImportOutcome.Unchanged, result.Outcome);
        Assert.True(result.PrintedOutput);
        Assert.Contains("Changes for 'Subject'", output);
        Assert.Contains("THE-DIFF", output);
        Assert.Contains("Nothing above is actually applicable", output);
    }
}

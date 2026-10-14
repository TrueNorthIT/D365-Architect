using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using Spectre.Console;

namespace D365Architect.Commands;

/// <summary>How one asset's preview turned out.</summary>
public enum ImportOutcome
{
    /// <summary>Nothing to import (already matches live, or nothing actionable).</summary>
    Unchanged,

    /// <summary>There is something to import; <see cref="PreparedImport.ApplyAsync"/> is set.</summary>
    Changed,

    /// <summary>The preview failed or a safety gate refused it. Already reported to the console; never applied.</summary>
    Failed,
}

/// <summary>
/// The result of previewing one asset: its outcome plus, when
/// <see cref="ImportOutcome.Changed"/>, a deferred apply. Lets a caller
/// (<c>solution import</c>) preview many assets, confirm once, then apply.
/// </summary>
/// <param name="Outcome">How the preview turned out.</param>
/// <param name="ApplyAsync">Writes the previewed change and prints its success line. Returns false (having reported why) if the write failed. Null unless <paramref name="Outcome"/> is <see cref="ImportOutcome.Changed"/>.</param>
public sealed record PreparedImport(ImportOutcome Outcome, Func<CancellationToken, Task<bool>>? ApplyAsync = null)
{
    /// <summary>True when previewing printed something (a diff, a "No changes" line) that a caller listing many assets should separate from the next one with a blank line.</summary>
    public bool PrintedOutput { get; init; }

    public static readonly PreparedImport Unchanged = new(ImportOutcome.Unchanged);
    public static readonly PreparedImport Failed = new(ImportOutcome.Failed);
}

/// <summary>
/// Runs the shared flow behind every `import` command (`form import`,
/// `table import`, `view import`, `choice import`): preview the change
/// against what's live, print a diff, then — unless blocked or run with
/// <c>--whatif</c> — confirm and apply it. Injected into each command (like
/// every other service in this app — see <c>Program.cs</c>) rather than
/// shared via a base class the commands would have to inherit from: each
/// command stays an ordinary <c>AsyncCommand&lt;TSettings&gt;</c> and hands
/// its own <see cref="IImportService{TInput,TPreview}"/> plus an
/// <see cref="ImportFlowSpec{TSettings,TInput,TPreview}"/> describing
/// whatever else is actually different about it — its diff body, which of
/// its own exceptions are user-facing, and so on.
///
/// The flow is split in two so <c>solution import</c> can ask once for many
/// assets: <see cref="PreviewAsync{TSettings,TInput,TPreview}"/> reads,
/// previews, prints the diff and applies every per-asset safety gate
/// (SkipBefore/SkipAfter/BlockReason) but never prompts or writes, returning
/// a <see cref="PreparedImport"/> with a deferred apply;
/// <see cref="RunAsync{TSettings,TInput,TPreview}"/> is preview + the single
/// prompt + apply, for the single-asset commands.
/// </summary>
public sealed class ImportRunner(IAuthenticationService authenticationService)
{
    public async Task<int> RunAsync<TSettings, TInput, TPreview>(TSettings settings, IImportService<TInput, TPreview> service, ImportFlowSpec<TSettings, TInput, TPreview> spec, CancellationToken cancellationToken)
        where TSettings : ImportSettingsBase
        where TInput : class
    {
        var prepared = await PreviewAsync(settings, service, spec, printUnchanged: true, cancellationToken);
        switch (prepared.Outcome)
        {
            case ImportOutcome.Failed:
                return 1;
            case ImportOutcome.Unchanged:
                return 0;
        }

        if (settings.WhatIf)
        {
            AnsiConsole.MarkupLine("[grey]--whatif: nothing was written.[/]");
            return 0;
        }

        if (!settings.Yes && !AnsiConsole.Confirm("Import these changes into Dataverse?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[yellow]Aborted.[/] Nothing was written.");
            return 0;
        }

        return await prepared.ApplyAsync!(cancellationToken) ? 0 : 1;
    }

    /// <summary>
    /// Reads and previews one asset and prints its diff, without prompting or
    /// writing. Known failures are reported and returned as
    /// <see cref="ImportOutcome.Failed"/>; unexpected exceptions propagate.
    /// </summary>
    /// <param name="printUnchanged">Whether to print the "No changes" line for an unchanged asset. Single imports always do; <c>solution import</c> only with <c>--verbose</c>.</param>
    public async Task<PreparedImport> PreviewAsync<TSettings, TInput, TPreview>(TSettings settings, IImportService<TInput, TPreview> service, ImportFlowSpec<TSettings, TInput, TPreview> spec, bool printUnchanged, CancellationToken cancellationToken)
        where TSettings : ImportSettingsBase
        where TInput : class
    {
        var input = await spec.ReadInputAsync(settings, cancellationToken);
        if (input is null)
        {
            return PreparedImport.Failed;
        }

        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);
            var preview = await AnsiConsole.Status().StartAsync(spec.PreviewStatusMessage(input),
                async _ => await service.PreviewAsync(auth.EnvironmentUrl, auth.AccessToken, input, cancellationToken));
            spec.OnPreviewBuilt?.Invoke(preview);

            if (spec.SkipBeforePrinting(preview) is { } beforeMessage)
            {
                if (printUnchanged)
                {
                    AnsiConsole.MarkupLine($"[green]No changes[/] — {beforeMessage}");
                }

                return PreparedImport.Unchanged with { PrintedOutput = printUnchanged };
            }

            // A "nothing applicable" outcome is an unchanged asset like any
            // other: silent unless the caller asked for unchanged output
            // (single imports always do; solution import only with --verbose).
            var afterMessage = spec.SkipAfterPrinting(preview);
            if (afterMessage is not null && !printUnchanged)
            {
                return PreparedImport.Unchanged;
            }

            AnsiConsole.MarkupLine($"[bold]Changes for '{spec.SubjectName(input)}':[/]");
            spec.PrintChanges(preview);

            if (afterMessage is not null)
            {
                AnsiConsole.MarkupLine(afterMessage);
                return PreparedImport.Unchanged with { PrintedOutput = true };
            }

            if (spec.BlockReason(settings, preview) is { } blockReason)
            {
                AnsiConsole.MarkupLine($"[red]Refusing to import.[/] {blockReason}");
                return PreparedImport.Failed;
            }

            return new PreparedImport(ImportOutcome.Changed, ct => ApplyAsync(spec, auth, input, preview, ct));
        }
        catch (Exception ex) when (TryReport(ex, spec, input))
        {
            return PreparedImport.Failed;
        }
    }

    private static async Task<bool> ApplyAsync<TSettings, TInput, TPreview>(ImportFlowSpec<TSettings, TInput, TPreview> spec, AuthenticatedContext auth, TInput input, TPreview preview, CancellationToken cancellationToken)
        where TSettings : ImportSettingsBase
        where TInput : class
    {
        try
        {
            await AnsiConsole.Status().StartAsync(spec.ApplyStatusMessage, async _ => await spec.ApplyAsync(auth, preview, cancellationToken));
            spec.PrintSuccess(input, preview);
            return true;
        }
        catch (Exception ex) when (TryReport(ex, spec, input))
        {
            return false;
        }
    }

    /// <summary>Prints the exception if it's one this flow knows how to report (auth, HTTP, or the command's own domain exceptions); false lets it propagate.</summary>
    private static bool TryReport<TSettings, TInput, TPreview>(Exception ex, ImportFlowSpec<TSettings, TInput, TPreview> spec, TInput input)
        where TSettings : ImportSettingsBase
        where TInput : class
    {
        switch (ex)
        {
            case AuthenticationRequiredException auth:
                ErrorConsole.Print(auth);
                return true;
            case HttpRequestException http:
                ErrorConsole.Print(http);
                return true;
        }

        if (spec.FormatDomainException(ex, input) is { } message)
        {
            AnsiConsole.MarkupLine(message);
            return true;
        }

        return false;
    }
}

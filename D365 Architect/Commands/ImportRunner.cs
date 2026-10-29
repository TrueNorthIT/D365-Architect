using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using Spectre.Console;

namespace D365Architect.Commands;

/// <summary>
/// Runs the shared flow behind every `import` command (`form import`,
/// `table import`, `view import`): preview the change against what's live,
/// print a diff, then — unless blocked or run with <c>--whatif</c> —
/// confirm and apply it. Injected into each command (like every other
/// service in this app — see <c>Program.cs</c>) rather than shared via a
/// base class the commands would have to inherit from: each command stays
/// an ordinary <c>AsyncCommand&lt;TSettings&gt;</c> and hands its own
/// <see cref="IImportService{TInput,TPreview}"/> plus an
/// <see cref="ImportFlowSpec{TSettings,TInput,TPreview}"/> describing
/// whatever else is actually different about it — its diff body, which of
/// its own exceptions are user-facing, and so on — to
/// <see cref="RunAsync{TSettings,TInput,TPreview}"/> to run. Previewing
/// needs no such per-command override: every import service's
/// <c>PreviewAsync</c> has the exact same shape, so this calls it directly.
/// With <c>--publish</c> (<see cref="PublishableImportSettings"/>) the same
/// preview decides whether the file is fully live, and the flow publishes
/// instead of applying.
/// </summary>
public sealed class ImportRunner(IAuthenticationService authenticationService)
{
    public async Task<int> RunAsync<TSettings, TInput, TPreview>(TSettings settings, IImportService<TInput, TPreview> service, ImportFlowSpec<TSettings, TInput, TPreview> spec, CancellationToken cancellationToken)
        where TSettings : ImportSettingsBase
        where TInput : class
    {
        var input = await spec.ReadInputAsync(settings, cancellationToken);
        if (input is null)
        {
            return 1;
        }

        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);
            var publish = settings is PublishableImportSettings { Publish: true };
            if (publish && spec.PublishPrecheckAsync is not null && await spec.PublishPrecheckAsync(auth, input, cancellationToken) is { } precheckReason)
            {
                return RefusePublish(precheckReason);
            }

            var preview = await AnsiConsole.Status().StartAsync(spec.PreviewStatusMessage(input),
                async _ => await service.PreviewAsync(auth.EnvironmentUrl, auth.AccessToken, input, cancellationToken));
            spec.OnPreviewBuilt?.Invoke(preview);

            if (publish)
            {
                return await PublishAsync(settings, spec, auth, preview, cancellationToken);
            }

            if (spec.SkipBeforePrinting(preview) is { } beforeMessage)
            {
                AnsiConsole.MarkupLine($"[green]No changes[/] — {beforeMessage}");
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]Changes for '{spec.SubjectName(input)}':[/]");
            spec.PrintChanges(preview);

            if (spec.SkipAfterPrinting(preview) is { } afterMessage)
            {
                AnsiConsole.MarkupLine(afterMessage);
                return 0;
            }

            if (spec.BlockReason(settings, preview) is { } blockReason)
            {
                AnsiConsole.MarkupLine($"[red]Refusing to import.[/] {blockReason}");
                return 1;
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

            await AnsiConsole.Status().StartAsync(spec.ApplyStatusMessage, async _ => await spec.ApplyAsync(auth, preview, cancellationToken));
            spec.PrintSuccess(input, preview);
            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (HttpRequestException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (Exception ex) when (spec.FormatDomainException(ex, input) is { } message)
        {
            AnsiConsole.MarkupLine(message);
            return 1;
        }
    }

    /// <summary>REQ-35's refusal: <paramref name="names"/> isn't live, so there's nothing to publish until <paramref name="command"/> has written it.</summary>
    public static string NotYetCreated(string names, string command) =>
        $"{names} doesn't exist yet — run `{command}` without --publish first.";

    /// <summary>REQ-36's refusal: publishing now would publish something other than the file.</summary>
    public static string UnappliedChanges(int count) =>
        $"the file has {count} unapplied change{(count == 1 ? "" : "s")} — run without --publish first.";

    /// <summary>
    /// <c>--publish</c> (REQ-34 to REQ-39): instead of applying <paramref name="preview"/>, refuse when it
    /// isn't fully live yet, otherwise say what would be published, confirm (unless <c>--yes</c>; never
    /// under <c>--whatif</c>) and publish only that. Writes nothing else.
    /// </summary>
    private static async Task<int> PublishAsync<TSettings, TInput, TPreview>(TSettings settings, ImportFlowSpec<TSettings, TInput, TPreview> spec, AuthenticatedContext auth, TPreview preview, CancellationToken cancellationToken)
        where TSettings : ImportSettingsBase
        where TInput : class
    {
        var publishAsync = spec.PublishAsync ?? throw new InvalidOperationException("--publish was set on a command whose import flow can't publish.");

        if (spec.PublishRefusal(preview) is { } refusal)
        {
            return RefusePublish(refusal);
        }

        AnsiConsole.MarkupLine($"would publish: {string.Join(", ", spec.PublishTargets(preview)).EscapeMarkup()}");

        if (settings.WhatIf)
        {
            AnsiConsole.MarkupLine("[grey]--whatif: nothing was published.[/]");
            return 0;
        }

        if (!settings.Yes && !AnsiConsole.Confirm("Publish these customizations in Dataverse?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[yellow]Aborted.[/] Nothing was published.");
            return 0;
        }

        await AnsiConsole.Status().StartAsync("Publishing...", async _ => await publishAsync(auth, preview, cancellationToken));
        AnsiConsole.MarkupLine("[green]Published.[/]");
        return 0;
    }

    private static int RefusePublish(string reason)
    {
        AnsiConsole.MarkupLine($"[red]Refusing to publish.[/] {reason.EscapeMarkup()}");
        return 1;
    }
}

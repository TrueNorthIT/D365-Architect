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
            var preview = await AnsiConsole.Status().StartAsync(spec.PreviewStatusMessage(input),
                async _ => await service.PreviewAsync(auth.EnvironmentUrl, auth.AccessToken, input, cancellationToken));
            spec.OnPreviewBuilt?.Invoke(preview);

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
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
        catch (Exception ex) when (spec.FormatDomainException(ex, input) is { } message)
        {
            AnsiConsole.MarkupLine(message);
            return 1;
        }
    }
}

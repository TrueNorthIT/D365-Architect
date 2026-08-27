using System.ComponentModel;
using D365Architect.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Environments;

/// <summary>
/// Sub-command example: `d365architect environment sync --environment dev`.
/// Shows an async command sharing the same injected service as its sibling
/// sub-command (<see cref="ListEnvironmentsCommand"/>).
/// </summary>
public sealed class SyncEnvironmentCommand(IEnvironmentService environments) : AsyncCommand<SyncEnvironmentCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-e|--environment <NAME>")]
        [Description("The environment to synchronise.")]
        public required string Environment { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await AnsiConsole.Status().StartAsync($"Syncing '{settings.Environment}'...",
            async _ => await environments.SyncAsync(settings.Environment, cancellationToken));

        AnsiConsole.MarkupLine($"[green]Done.[/] Synced '{settings.Environment}'.");
        return 0;
    }
}

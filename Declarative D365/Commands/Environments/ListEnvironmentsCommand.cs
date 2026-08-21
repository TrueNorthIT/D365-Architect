using Declarative_D365.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Declarative_D365.Commands.Environments;

/// <summary>
/// Sub-command example: `d365cli environment list`.
/// </summary>
public sealed class ListEnvironmentsCommand(IEnvironmentService environments) : Command<ListEnvironmentsCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        foreach (var name in environments.ListEnvironments())
        {
            AnsiConsole.MarkupLine($"- {name}");
        }

        return 0;
    }
}

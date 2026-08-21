using System.ComponentModel;
using D365Architect.Services.Authentication;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Auth;

/// <summary>
/// `d365architect auth login --url https://yourorg.crm.dynamics.com`
/// Opens the system browser for an interactive sign-in and remembers it
/// for later commands (e.g. `whoami`) in this and future CLI invocations.
/// </summary>
public sealed class LoginCommand(IAuthenticationService authenticationService) : AsyncCommand<LoginCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-u|--url <URL>")]
        [Description("The URL of the D365 environment to sign in to, e.g. https://yourorg.crm.dynamics.com")]
        public required string Url { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var environmentUrl) || environmentUrl.Scheme != Uri.UriSchemeHttps)
        {
            AnsiConsole.MarkupLine($"[red]'{settings.Url}' is not a valid https:// environment URL.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("Opening your browser to sign in...");

        try
        {
            var result = await authenticationService.LoginInteractiveAsync(environmentUrl, cancellationToken);
            AnsiConsole.MarkupLine($"[green]Signed in[/] as {result.Username} to {result.EnvironmentUrl}");
            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return 1;
        }
    }
}

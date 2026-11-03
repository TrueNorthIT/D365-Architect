using D365Architect.Services.Authentication;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands;

/// <summary>
/// `d365architect whoami` — shows who you're currently signed in as, and to
/// which D365 environment. Requires a prior `auth login`, or the
/// d365architect_AUTH_MODE environment variables for a non-interactive sign-in.
/// </summary>
public sealed class WhoAmICommand(IAuthenticationService authenticationService, IDataverseClient dataverseClient)
    : AsyncCommand<WhoAmICommand.Settings>
{
    /// <summary>
    /// Registers this command with the top-level configurator. Called from
    /// Program.cs — the command owns its own registration, Program.cs just
    /// wires it in.
    /// </summary>
    public static void Configure(IConfigurator config)
    {
        config.AddCommand<WhoAmICommand>("whoami")
            .WithDescription("Shows who you're currently signed in as, and to which D365 environment.");
    }

    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);
            var who = await dataverseClient.WhoAmIAsync(auth.EnvironmentUrl, auth.AccessToken, cancellationToken);
            var fullName = await dataverseClient.TryGetUserFullNameAsync(auth.EnvironmentUrl, auth.AccessToken, who.UserId, cancellationToken);

            if (auth.AuthMode is { } mode)
            {
                // Make it obvious this came from environment variables, not
                // `auth login` — otherwise a d365architect_AUTH_MODE left set
                // in a shell is easy to mistake for the saved sign-in.
                AnsiConsole.MarkupLine(
                    $"Non-interactive sign-in: signed in via [bold]{NonInteractiveAuthSettingsResolver.GetModeName(mode)}[/] as {auth.Username.EscapeMarkup()} to {auth.EnvironmentUrl.ToString().EscapeMarkup()}");
            }

            AnsiConsole.MarkupLine($"[bold]User:[/] {fullName ?? auth.Username} ({auth.Username})");
            AnsiConsole.MarkupLine($"[bold]User Id:[/] {who.UserId}");
            AnsiConsole.MarkupLine($"[bold]Business Unit Id:[/] {who.BusinessUnitId}");
            AnsiConsole.MarkupLine($"[bold]Organization Id:[/] {who.OrganizationId}");
            AnsiConsole.MarkupLine($"[bold]Environment:[/] {auth.EnvironmentUrl}");
            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
    }
}

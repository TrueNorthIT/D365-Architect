using System.ComponentModel;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Security;

/// <summary>
/// `d365architect security export --solution examplesolution [--output .] [--table t]... [--role "Role name"]...`
/// Exports the security of one solution's roles as YAML, in the layout <c>solution export</c> writes:
/// <c>&lt;output&gt;/&lt;solution&gt;/&lt;table&gt;/&lt;table&gt;.security.yml</c> for each table at least one of the solution's roles can
/// reach, and <c>&lt;output&gt;/&lt;solution&gt;/roles/&lt;role-name&gt;.role.yml</c> for each of the solution's roles. See
/// <see cref="ISecurityExportService"/> for what goes in which file and how <c>--table</c>/<c>--role</c> limit it.
/// </summary>
public sealed class ExportSecurityCommand(IAuthenticationService authenticationService, ISecurityExportService securityExportService)
    : AsyncCommand<ExportSecurityCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of the solution whose roles and tables are exported.")]
        public required string Solution { get; init; }

        [CommandOption("-o|--output <DIRECTORY>")]
        [Description("Directory to create the solution's own folder under. Defaults to the current directory.")]
        public string? Output { get; init; }

        [CommandOption("-t|--table <LOGICAL_NAME>")]
        [Description("Limit the export to this table's security file (logical name, case-insensitive). Repeat for several. Without --role, no role files are written. Errors if a name matches no table in the solution.")]
        public string[] Tables { get; init; } = [];

        [CommandOption("-r|--role <NAME>")]
        [Description("Limit the export to this role's file (role name, case-insensitive). Repeat for several. Without --table, no table security files are written. Errors if a name matches no role in the solution.")]
        public string[] Roles { get; init; } = [];
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);

            var result = await AnsiConsole.Status().StartAsync($"Exporting the security of solution '{settings.Solution}'...",
                async _ => await securityExportService.ExportSecurityAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Solution, settings.Tables, settings.Roles, cancellationToken));

            var solutionDirectory = Path.Combine(settings.Output ?? ".", settings.Solution);
            foreach (var table in result.Tables)
            {
                var tableDirectory = Path.Combine(solutionDirectory, table.TableLogicalName);
                Directory.CreateDirectory(tableDirectory);
                var path = Path.Combine(tableDirectory, $"{table.TableLogicalName}.security.yml");
                await File.WriteAllTextAsync(path, table.Yaml, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {path.EscapeMarkup()}");
            }

            await WriteRoleFilesAsync(solutionDirectory, result.Roles, cancellationToken);

            AnsiConsole.MarkupLine($"[green]Done.[/] Exported {result.Tables.Count} table security file(s) and {result.Roles.Count} role file(s) to {solutionDirectory.EscapeMarkup()}");
            return 0;
        }
        catch (AuthenticationRequiredException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (UnknownTableException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (UnknownRoleException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (SolutionNotFoundException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
        catch (InvalidDataException ex)
        {
            ErrorConsole.Print($"Couldn't parse the security metadata for solution '{settings.Solution}': {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            ErrorConsole.Print(ex);
            return 1;
        }
    }

    /// <summary>Writes each role file to <c>&lt;solution folder&gt;/roles/&lt;stem&gt;.role.yml</c>. Shared with <c>solution export</c>.</summary>
    internal static async Task WriteRoleFilesAsync(string solutionDirectory, IReadOnlyList<ExportedRole>? roles, CancellationToken cancellationToken)
    {
        if (roles is not { Count: > 0 })
        {
            return;
        }

        var rolesDirectory = Path.Combine(solutionDirectory, SolutionImportOrchestrator.RolesFolderName);
        Directory.CreateDirectory(rolesDirectory);
        foreach (var role in roles)
        {
            var path = Path.Combine(rolesDirectory, $"{role.FileNameStem}.role.yml");
            await File.WriteAllTextAsync(path, role.Yaml, cancellationToken);
            AnsiConsole.MarkupLine($"[green]Exported.[/] Wrote {path.EscapeMarkup()}");
        }
    }
}

using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Spectre.Console;

namespace D365Architect.Commands.Security;

/// <summary>The two kinds of security file: a table's <c>*.security.yml</c> and a role's <c>*.role.yml</c>.</summary>
public enum SecurityFileKind
{
    TableSecurity,
    Role,
}

/// <summary>
/// Previews one security file against the live roles and prints what would change, without prompting or writing, returning a
/// <see cref="PreparedImport"/> whose deferred apply writes exactly that. Shared by <c>security import</c> and <c>solution import</c>,
/// which both collect previews and confirm once (<see cref="Solution.SolutionImportFlow"/>).
///
/// The printed plan: in a table file every one of the eight privileges is always present, so a change is shown as
/// <c>~ Delete: None → User</c>; in a role file a privilege the role doesn't hold yet is <c>+ prvBulkDelete: User</c> and a changed level
/// <c>~ prvExportToExcel: Organisation → User</c>. A privilege held live but missing (or None) in the file is
/// <c>~ would remove Share: User (kept)</c> without <c>--allow-removals</c>, and <c>- Share: User (remove)</c> with it. Kept privileges are
/// always reported, even when nothing else changed; they are not changes, so they never make the file count towards the prompt.
/// </summary>
internal sealed class SecurityFilePreviewer(ISecurityImportService securityImportService, Func<CancellationToken, Task<AuthenticatedContext>> getContextAsync)
{
    /// <summary>The file's kind, from its name: <c>*.security.yml</c> or <c>*.role.yml</c>. Null for anything else.</summary>
    public static SecurityFileKind? KindOf(string path) =>
        path.EndsWith(".security.yml", StringComparison.OrdinalIgnoreCase) ? SecurityFileKind.TableSecurity
        : path.EndsWith(".role.yml", StringComparison.OrdinalIgnoreCase) ? SecurityFileKind.Role
        : null;

    /// <param name="path">The file to preview.</param>
    /// <param name="kind">Which kind of file it is.</param>
    /// <param name="solutionUniqueName">The solution a role file belongs to (required for <see cref="SecurityFileKind.Role"/>; its tables are the ones a role file never touches).</param>
    /// <param name="allowRemovals">Whether a privilege held live but missing from the file is removed (otherwise kept and reported).</param>
    /// <param name="printUnchanged">Whether to print a "No changes" line for a file that already matches live.</param>
    /// <param name="cancellationToken"></param>
    public async Task<PreparedImport> PreviewAsync(string path, SecurityFileKind kind, string? solutionUniqueName, bool allowRemovals, bool printUnchanged, CancellationToken cancellationToken)
    {
        object? input = kind == SecurityFileKind.TableSecurity
            ? await YamlFileReader.TryReadAsync(path, "table security file", TableSecurityYamlDeserializer.FromYaml, cancellationToken)
            : await YamlFileReader.TryReadAsync(path, "role file", RoleYamlDeserializer.FromYaml, cancellationToken);
        if (input is null)
        {
            return PreparedImport.Failed;
        }

        if (kind == SecurityFileKind.Role && string.IsNullOrWhiteSpace(solutionUniqueName))
        {
            ErrorConsole.Print($"'{path}' is a role file, which needs --solution: the solution's tables are the ones a role file leaves to their own security files.");
            return PreparedImport.Failed;
        }

        AuthenticatedContext auth;
        SecurityImportPreview preview;
        try
        {
            auth = await getContextAsync(cancellationToken);
            preview = input switch
            {
                TableSecurityDefinition table => await AnsiConsole.Status().StartAsync($"Comparing '{table.Table}' security with the live roles...",
                    async _ => await securityImportService.PreviewTableSecurityAsync(auth.EnvironmentUrl, auth.AccessToken, table, allowRemovals, cancellationToken)),
                RoleDefinition role => await AnsiConsole.Status().StartAsync($"Comparing role '{role.Name}' with the live role...",
                    async _ => await securityImportService.PreviewRoleAsync(auth.EnvironmentUrl, auth.AccessToken, role, solutionUniqueName!, allowRemovals, cancellationToken)),
                _ => throw new InvalidOperationException($"Unexpected security input {input.GetType().Name}."),
            };
        }
        catch (Exception ex) when (TryReport(ex, path))
        {
            return PreparedImport.Failed;
        }

        var subject = kind == SecurityFileKind.TableSecurity ? $"'{preview.SubjectName}' (table security)" : $"role '{preview.SubjectName}'";
        if (preview.Roles.All(r => r.Changes.Count == 0))
        {
            if (printUnchanged)
            {
                AnsiConsole.MarkupLine($"[green]No changes[/] — {subject.EscapeMarkup()} already matches what's live in Dataverse. Nothing to import.");
            }

            return PreparedImport.Unchanged with { PrintedOutput = printUnchanged };
        }

        AnsiConsole.MarkupLine($"[bold]Changes for {subject.EscapeMarkup()}:[/]");
        foreach (var plan in preview.Roles.Where(r => r.Changes.Count > 0))
        {
            if (kind == SecurityFileKind.TableSecurity)
            {
                AnsiConsole.MarkupLine($"  [bold]{plan.RoleName.EscapeMarkup()}[/]");
            }

            foreach (var change in plan.Changes)
            {
                AnsiConsole.MarkupLine(FormatChange(change, kind));
            }
        }

        if (!preview.HasChanges)
        {
            AnsiConsole.MarkupLine("[grey]Nothing to import: every difference above is a privilege kept because --allow-removals wasn't given.[/]");
            return PreparedImport.Unchanged with { PrintedOutput = true };
        }

        return new PreparedImport(ImportOutcome.Changed, ct => ApplyAsync(auth, preview, subject, path, ct));
    }

    private async Task<bool> ApplyAsync(AuthenticatedContext auth, SecurityImportPreview preview, string subject, string path, CancellationToken cancellationToken)
    {
        try
        {
            await AnsiConsole.Status().StartAsync("Importing...", async _ => await securityImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, cancellationToken));
            var changes = preview.Roles.SelectMany(r => r.Changes).ToList();
            var set = changes.Count(c => c.Action == PrivilegeChangeAction.Set);
            var removed = changes.Count(c => c.Action == PrivilegeChangeAction.Remove);
            AnsiConsole.MarkupLine($"[green]Imported.[/] {subject.EscapeMarkup()}: {set} privilege(s) set, {removed} removed.");
            return true;
        }
        catch (Exception ex) when (TryReport(ex, path))
        {
            return false;
        }
    }

    /// <summary>One line of the plan. Table-file privileges are named by right ("Delete"); role-file ones by platform name or <c>table.right</c> (e.g. "account.read").</summary>
    internal static string FormatChange(PrivilegeChange change, SecurityFileKind kind)
    {
        var name = (kind == SecurityFileKind.TableSecurity
            ? change.Privilege.TableRight?.ToString() ?? change.Privilege.PrivilegeName ?? ""
            : change.Privilege.IsTablePrivilege
                ? $"{change.Privilege.TableLogicalName}.{CamelCase(change.Privilege.TableRight?.ToString() ?? "")}"
                : change.Privilege.PrivilegeName ?? "").EscapeMarkup();

        return change.Action switch
        {
            PrivilegeChangeAction.Set when kind == SecurityFileKind.Role && change.From == SecurityAccessLevel.None => $"[green]    + {name}: {change.To}[/]",
            PrivilegeChangeAction.Set => $"[yellow]    ~ {name}: {change.From} → {change.To}[/]",
            PrivilegeChangeAction.Remove => $"[red]    - {name}: {change.From} (remove)[/]",
            _ => $"[grey]    ~ would remove {name}: {change.From} (kept)[/]",
        };
    }

    private static string CamelCase(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static bool TryReport(Exception ex, string path)
    {
        switch (ex)
        {
            case AuthenticationRequiredException auth:
                ErrorConsole.Print(auth);
                return true;
            case RoleNotFoundException or SolutionNotFoundException or HttpRequestException or InvalidDataException:
                ErrorConsole.Print($"'{path}' failed: {ex.Message}");
                return true;
            default:
                return false;
        }
    }
}

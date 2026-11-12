using System.ComponentModel;
using D365Architect.Commands.Solution;
using D365Architect.Services.Authentication;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Security;

/// <summary>
/// `d365architect security import --input ./examplesolution [--solution examplesolution] [--table t]... [--role "Role name"]... [--allow-removals] [--yes] [--whatif] [--verbose]`
/// Writes table security files (<c>*.security.yml</c>) and role files (<c>*.role.yml</c>) back into Dataverse by changing what each named
/// role holds. <c>--input</c> is a solution folder written by <c>solution export</c>/<c>security export</c> (every
/// <c>&lt;table&gt;/*.security.yml</c>, then every <c>roles/*.role.yml</c>) or a single file.
///
/// Every file is previewed first (its plan printed; see <see cref="SecurityFilePreviewer"/>), then it asks once ("Import N changes?",
/// N being the changed files) and applies them all, as <c>solution import</c> does. <c>--yes</c> skips the prompt; <c>--whatif</c> prints the
/// plans and never prompts or writes. A file that fails (e.g. it names a role that doesn't exist) is reported and never applied, the others
/// still are, and the exit code is non-zero.
///
/// Roles are found by name and never created. A privilege held live but missing (or None) in a file is only removed with
/// <c>--allow-removals</c>; otherwise it is kept and reported. A role file needs <c>--solution</c>: the solution's tables are left to their own
/// security files, and every other table is the role file's business (see <see cref="ISecurityImportService"/>).
///
/// <c>--table</c> and <c>--role</c> limit a folder import the way they limit <c>security export</c>: <c>--table</c> alone imports only those
/// tables' security files, <c>--role</c> alone only those roles' files, both together both. A <c>--table</c> with no folder, or a
/// <c>--role</c> that is not a role of the solution, is an error and nothing is imported.
/// </summary>
public sealed class ImportSecurityCommand(IAuthenticationService authenticationService, ISecurityImportService securityImportService, IDataverseClient dataverseClient)
    : AsyncCommand<ImportSecurityCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <PATH>")]
        [Description("A solution folder written by 'solution export' or 'security export' (every <table>/*.security.yml and roles/*.role.yml in it), or a single *.security.yml or *.role.yml file.")]
        public required string Input { get; init; }

        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of the solution the files belong to. Required for role files and --role: a role file never touches the solution's own tables, which are looked up in Dataverse.")]
        public string? Solution { get; init; }

        [CommandOption("-t|--table <LOGICAL_NAME>")]
        [Description("Limit a folder import to this table's security file (folder name, case-insensitive). Repeat for several. Without --role, role files are skipped. Errors if a name matches no table folder.")]
        public string[] Tables { get; init; } = [];

        [CommandOption("-r|--role <NAME>")]
        [Description("Limit a folder import to this role's file (role name, case-insensitive). Repeat for several. Without --table, table security files are skipped. Needs --solution; errors if a name matches no role in the solution.")]
        public string[] Roles { get; init; } = [];

        [CommandOption("--allow-removals")]
        [Description("Remove a privilege the role holds live but the file leaves out or sets to None. Without it such a privilege is kept and reported.")]
        public bool AllowRemovals { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt and import every changed file immediately.")]
        public bool Yes { get; init; }

        [CommandOption("--whatif")]
        [Description("Only show each file's plan — never prompt and never write anything to Dataverse.")]
        public bool WhatIf { get; init; }

        [CommandOption("--verbose")]
        [Description("When importing a folder, also print a 'No changes' line for every file that already matches what's live. Unchanged files are silent by default.")]
        public bool Verbose { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tablesGiven = settings.Tables.Length > 0;
        var rolesGiven = settings.Roles.Length > 0;
        var isFile = File.Exists(settings.Input);

        if (!isFile && !Directory.Exists(settings.Input))
        {
            ErrorConsole.Print($"'{settings.Input}' doesn't exist.");
            return 1;
        }

        if (rolesGiven && string.IsNullOrWhiteSpace(settings.Solution))
        {
            ErrorConsole.Print($"--role needs --solution: the role names are checked against the solution's roles.");
            return 1;
        }

        List<(string Path, SecurityFileKind Kind)> files;
        if (isFile)
        {
            if (tablesGiven || rolesGiven)
            {
                ErrorConsole.Print($"--table and --role limit a folder import; leave them out when --input is a single file.");
                return 1;
            }

            if (SecurityFilePreviewer.KindOf(settings.Input) is not { } kind)
            {
                ErrorConsole.Print($"'{settings.Input}' is neither a *.security.yml nor a *.role.yml file.");
                return 1;
            }

            files = [(settings.Input, kind)];
        }
        else
        {
            var selected = await SelectFolderFilesAsync(settings, tablesGiven, rolesGiven, cancellationToken);
            if (selected is null)
            {
                return 1;
            }

            files = selected;
        }

        if (files.Count == 0)
        {
            ErrorConsole.Warn($"No *.security.yml or *.role.yml files to import under '{settings.Input}'.");
            return 0;
        }

        if (files.Any(f => f.Kind == SecurityFileKind.Role) && string.IsNullOrWhiteSpace(settings.Solution))
        {
            ErrorConsole.Print($"Role files need --solution: the solution's tables are the ones a role file leaves to their own security files. Pass --solution, or --table to import only table security files.");
            return 1;
        }

        var previewer = new SecurityFilePreviewer(securityImportService, authenticationService.GetCurrentContextAsync);
        var printUnchanged = isFile || settings.Verbose;
        var batch = new SecurityImportBatch();
        var flow = new SolutionImportFlow(printUnchanged);
        foreach (var (path, kind) in files)
        {
            // In a folder, a table's security file must be in that table's folder.
            var expectedTable = !isFile && kind == SecurityFileKind.TableSecurity ? Path.GetFileName(Path.GetDirectoryName(path)) : null;
            await flow.PreviewAsync(new SolutionImportItem(path, (_, ct) => previewer.PreviewAsync(path, kind, settings.Solution, settings.AllowRemovals, printUnchanged, ct, expectedTable, batch)), cancellationToken);
        }

        // The cross-file step: files disagreeing about one privilege (a shared one included) fail before anything is confirmed.
        SecurityConflictCheck.Apply(batch, flow);
        return await flow.ConfirmAndApplyAsync(settings.Yes, settings.WhatIf, confirm: null, cancellationToken);
    }

    /// <summary>
    /// The files of a solution folder to import, filtered by <c>--table</c>/<c>--role</c>: table security files first (by folder), then role
    /// files. Null (having reported why) when a filter names something that isn't there.
    /// </summary>
    private async Task<List<(string Path, SecurityFileKind Kind)>?> SelectFolderFilesAsync(Settings settings, bool tablesGiven, bool rolesGiven, CancellationToken cancellationToken)
    {
        var tableDirectories = SolutionImportOrchestrator.TableDirectories(settings.Input).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        if (tablesGiven)
        {
            try
            {
                var names = SolutionTableFilter.Select(tableDirectories.Select(d => Path.GetFileName(d)), settings.Tables, $"under '{settings.Input}'");
                tableDirectories = tableDirectories.Where(d => names.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)).ToList();
            }
            catch (UnknownTableException ex)
            {
                ErrorConsole.Print(ex);
                return null;
            }
        }

        HashSet<string>? roleNames = null;
        if (rolesGiven)
        {
            try
            {
                var auth = await authenticationService.GetCurrentContextAsync(cancellationToken);
                var solutionRoles = await dataverseClient.TryGetSolutionRolesAsync(auth.EnvironmentUrl, auth.AccessToken, settings.Solution!, cancellationToken)
                    ?? throw new SolutionNotFoundException(settings.Solution!);
                roleNames = SecurityExportService.SelectRoles(solutionRoles, settings.Roles, settings.Solution!)
                    .Select(r => r.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is UnknownRoleException or SolutionNotFoundException or AuthenticationRequiredException or HttpRequestException)
            {
                ErrorConsole.Print(ex);
                return null;
            }
        }

        var files = new List<(string Path, SecurityFileKind Kind)>();

        // Neither filter = both kinds of file; one filter = only that kind; both = both, each limited.
        if (tablesGiven || !rolesGiven)
        {
            files.AddRange(tableDirectories
                .SelectMany(d => Directory.GetFiles(d, "*.security.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                .Select(p => (p, SecurityFileKind.TableSecurity)));
        }

        var rolesDirectory = Path.Combine(settings.Input, SolutionImportOrchestrator.RolesFolderName);
        if ((rolesGiven || !tablesGiven) && Directory.Exists(rolesDirectory))
        {
            foreach (var path in Directory.GetFiles(rolesDirectory, "*.role.yml").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                // A role is told by the name inside its file, not by the file's name. A file that can't be read is kept, so its preview reports it.
                if (roleNames is not null && ReadRoleName(path) is { } name && !roleNames.Contains(name))
                {
                    continue;
                }

                files.Add((path, SecurityFileKind.Role));
            }
        }

        return files;
    }

    private static string? ReadRoleName(string path)
    {
        try
        {
            return RoleYamlDeserializer.FromYaml(File.ReadAllText(path)).Name;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }
}

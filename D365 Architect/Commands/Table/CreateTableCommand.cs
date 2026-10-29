using System.ComponentModel;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Table;

/// <summary>
/// `d365architect table create --input tn_project.table.yml [--solution TNCore] [--yes] [--whatif]`
/// Creates the table (and only its primary name column) described by a
/// `*.table.yml` when it doesn't exist yet; other columns are left for
/// `table import`, which already plans, diffs and creates every column type
/// it supports. Needs sign-in.
///
/// A table that already exists is reported ("... already exists, nothing
/// to create") and left alone, exit 0 - so this is safe to run on every
/// file in a solution, which is what `solution import` does. Before
/// creating, the file is checked by <see cref="TableCreateValidator"/>:
/// every value the create needs (<c>schemaName</c>, <c>ownershipType</c>,
/// <c>primaryNameAttribute</c>, both display names) must be in the file,
/// never guessed. Pass <c>--solution</c> to add the new table to that
/// solution as it's created (the same <c>MSCRM.SolutionUniqueName</c> gap
/// `table import --solution` closes for columns).
///
/// Follows <see cref="ImportTableCommand"/>'s shape: the shared
/// <see cref="ImportRunner"/> flow (plan, confirm unless <c>--yes</c>,
/// <c>--whatif</c> writes nothing), a public <see cref="RunAsync"/> so
/// `solution import` can call it, and it never rewrites the input file.
/// Doesn't publish, same as `table import`.
/// </summary>
public sealed class CreateTableCommand(ITableCreateService tableCreateService, ImportRunner importRunner)
    : AsyncCommand<CreateTableCommand.Settings>
{
    public sealed class Settings : ImportSettingsBase
    {
        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of a solution to add the new table to as it is created.")]
        public string? Solution { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    /// <summary>The command's own logic, independent of Spectre's <see cref="CommandContext"/> - what `solution import` calls per <c>*.table.yml</c>.</summary>
    public async Task<int> RunAsync(Settings settings, CancellationToken cancellationToken)
    {
        var spec = new ImportFlowSpec<Settings, EntityDefinition, TableCreatePreview>
        {
            ReadInputAsync = (s, ct) => YamlFileReader.TryReadAsync(s.Input, "table", EntityYamlDeserializer.FromYaml, ct),

            SubjectName = entity => entity.LogicalName,

            PreviewStatusMessage = entity => $"Checking whether '{entity.LogicalName}' exists...",

            SkipBeforePrinting = preview => preview.AlreadyExists
                ? $"{preview.EntityLogicalName.EscapeMarkup()} already exists, nothing to create."
                : null,

            PrintChanges = preview =>
            {
                AnsiConsole.MarkupLine($"[green]  + table {preview.EntityLogicalName.EscapeMarkup()} (create)[/]");
                AnsiConsole.MarkupLine($"[green]  + {preview.PrimaryNameColumn?.EscapeMarkup()} (primary name column, created with the table)[/]");
                if (preview.DeferredColumns.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[grey]  Left for 'table import': {string.Join(", ", preview.DeferredColumns).EscapeMarkup()}[/]");
                }

                AnsiConsole.WriteLine();
            },

            ApplyStatusMessage = "Creating...",

            ApplyAsync = (auth, preview, ct) => tableCreateService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, settings.Solution, ct),

            PrintSuccess = (entity, preview) =>
            {
                AnsiConsole.MarkupLine($"[green]Created.[/] '{entity.LogicalName.EscapeMarkup()}' with its primary name column '{preview.PrimaryNameColumn?.EscapeMarkup()}'.");
                if (preview.DeferredColumns.Count > 0)
                {
                    AnsiConsole.MarkupLine("[grey]Run 'table import' on the same file to add its other columns.[/]");
                }

                AnsiConsole.MarkupLine("[grey]Note: this tool doesn't publish yet — publish customizations separately before end users see the table.[/]");
            },

            FormatDomainException = (ex, _) => ex switch
            {
                TableCreateValidationException or TableCreateFailedException => $"[red]{ex.Message.EscapeMarkup()}[/]",
                _ => null,
            },
        };

        return await importRunner.RunAsync(settings, tableCreateService, spec, cancellationToken);
    }
}

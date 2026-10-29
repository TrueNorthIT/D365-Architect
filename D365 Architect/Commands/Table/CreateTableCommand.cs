using System.ComponentModel;
using D365Architect.Services.Conversion;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Table;

/// <summary>
/// `d365architect table create --input tn_project.table.yml [--solution TNCore] [--yes] [--whatif]`
/// Creates the table (and only its primary name column) described by a
/// `*.table.yml` when it doesn't exist yet; other columns are left for
/// `table import`. Stub added by the test-first pass; behaviour pinned by
/// <c>CreateTableCommandTests</c>. Follows <see cref="ImportTableCommand"/>'s
/// shape: shared <see cref="ImportRunner"/> flow, public <c>RunAsync</c> so
/// `solution import` can call it, never rewrites the input file.
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

    public Task<int> RunAsync(Settings settings, CancellationToken cancellationToken) => throw new NotImplementedException();
}

using Spectre.Console.Cli;

namespace D365Architect.Commands.Solution;

/// <summary>
/// Owns the `solution` branch: creates it and registers every sub-command
/// under it, colocated with the command classes themselves. Called from
/// Program.cs like any other top-level registration.
/// </summary>
internal static class SolutionCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("solution", branch =>
        {
            branch.SetDescription("Work with a whole D365 solution — every table, view, form, and global choice it customizes.");

            branch.AddCommand<ExportSolutionCommand>("export")
                .WithDescription("Fetches every table/view/form/global choice a solution customizes and saves them as YAML under one folder per solution, with one sub-folder per table.")
                .WithExample("solution", "export", "--solution", "examplesolution");

            branch.AddCommand<ImportSolutionCommand>("import")
                .WithDescription("Walks a folder previously written by 'solution export' and imports every table/view/form/choice file found, each with its own diff and confirmation.")
                .WithExample("solution", "import", "--input", "./examplesolution", "--solution", "examplesolution");
        });
    }
}

using Spectre.Console.Cli;

namespace D365Architect.Commands.Choice;

internal static class ChoiceCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("choice", branch =>
        {
            branch.SetDescription("Work with D365 global choice (option set) definitions.");
            branch.AddCommand<ExportChoiceCommand>("export")
                .WithDescription("Fetches every global choice (or, with --solution, just one solution's) and saves them all as one YAML file.")
                .WithExample("choice", "export", "--solution", "examplesolution");
            branch.AddCommand<ImportChoiceCommand>("import")
                .WithDescription("Writes a *.choice.yml file's global choices back into Dataverse, after showing a diff and asking for confirmation. Creates any choice that doesn't exist live yet.")
                .WithExample("choice", "import", "--input", "choices.yml");
        });
    }
}

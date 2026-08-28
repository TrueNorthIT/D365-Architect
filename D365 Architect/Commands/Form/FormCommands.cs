using Spectre.Console.Cli;

namespace D365Architect.Commands.Form;

/// <summary>
/// Owns the `form` branch: creates it and registers every sub-command
/// under it, colocated with the command classes themselves. Called from
/// Program.cs like any other top-level registration.
/// </summary>
internal static class FormCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("form", branch =>
        {
            branch.SetDescription("Work with D365 form definitions.");

            branch.AddCommand<ExportFormCommand>("export")
                .WithDescription("Fetches one form from a table and saves it as YAML — pick it interactively, or pass --form-id.")
                .WithExample("form", "export", "--table", "account")
                .WithExample("form", "export", "--table", "account", "--form-id", "00000000-0000-0000-0000-000000000000");

            branch.AddCommand<BuildFormXmlCommand>("build-xml")
                .WithDescription("Rebuilds FormXML from a *.form.yml file, patched onto the form's current live FormXML.")
                .WithExample("form", "build-xml", "--input", "account-main-form.form.yml");
        });
    }
}

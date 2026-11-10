using Spectre.Console.Cli;

namespace D365Architect.Commands.Security;

/// <summary>Owns the `security` branch: a solution's security roles, as table security files and role files.</summary>
internal static class SecurityCommands
{
    public static void Configure(IConfigurator config)
    {
        config.AddBranch<CommandSettings>("security", branch =>
        {
            branch.SetDescription("Work with the security roles of a D365 solution — who can do what to each table, and each role's other privileges.");

            branch.AddCommand<ExportSecurityCommand>("export")
                .WithDescription("Fetches the privileges of a solution's roles and saves them as a *.security.yml in each table's folder and a *.role.yml per role under roles/.")
                .WithExample("security", "export", "--solution", "examplesolution")
                .WithExample("security", "export", "--solution", "examplesolution", "--role", "HIS User");

            branch.AddCommand<ImportSecurityCommand>("import")
                .WithDescription("Writes *.security.yml and *.role.yml files back into Dataverse by changing what each named role holds, after showing the plan and asking once.")
                .WithExample("security", "import", "--input", "./examplesolution", "--solution", "examplesolution")
                .WithExample("security", "import", "--input", "./examplesolution/tn_complaint/tn_complaint.security.yml");
        });
    }
}

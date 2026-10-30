using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Commands.Form;

/// <summary>
/// <c>form import</c> as a user meets it when the form already exists (issue #58): the plan, the safety gates
/// (<c>--allow-removals</c>, <c>--allow-unexpected</c>) and what is sent to Dataverse. The real command tree, import
/// runner and <see cref="FormImportService"/> run; only Dataverse (<see cref="FakeDataverseClient"/>), sign-in and the
/// console are faked. The live form is <see cref="SmallLive"/>.
/// In the "Console" collection because the command tree writes to <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ImportFormCommandPatchTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    /// <summary>Runs <c>form import --input &lt;yaml&gt; --yes</c> plus <paramref name="extraArguments"/> against the small live form.</summary>
    private async Task<(int ExitCode, string Output, FakeDataverseClient Client)> ImportAsync(FormDefinition yaml, params string[] extraArguments)
    {
        var path = _directory.File("form.form.yml");
        File.WriteAllText(path, FormYamlSerializer.ToYaml(yaml));

        using var console = new ConsoleHarness(); // --yes never prompts
        var host = FormCliHost.SignedIn();
        host.Client.SystemForm = new ExistingSystemForm(SmallFormId, SmallLive().ToString(SaveOptions.DisableFormatting), "tn_test", SmallFormName);

        var exitCode = await host.RunAsync(console, ["form", "import", "--input", path, "--yes", .. extraArguments]);
        return (exitCode, console.Output, host.Client);
    }

    private static XElement Written(FakeDataverseClient client) => XElement.Parse(Assert.Single(client.UpdateSystemFormXmlCalls).FormXml);

    /// <summary>The YAML a person edits: one label renamed (so there is something to import) and tn_fax deleted.</summary>
    private static FormDefinition FaxDeletedAndEmailRenamed() => ModelOf(SmallLive())
        .WithoutControl("tn_fax")
        .EditControl("tn_email", c => c.With(nameof(FormControl.Label), "Email address"));

    // ---- removal is explicit ----

    // Example: tn_fax deleted from the YAML, tn_email renamed -> the plan lists tn_fax as "would remove"; the form is written
    // with the rename and tn_fax still on it.
    [Fact]
    public async Task FormImport_FieldDeletedFromTheYaml_PlanListsItAsWouldRemoveAndTheFieldStaysOnTheForm()
    {
        var (exitCode, output, client) = await ImportAsync(FaxDeletedAndEmailRenamed());

        Expect.Equal(0, exitCode, $"The import should succeed. Output: <{output}>");
        Expect.Contains("would remove", output.ToLowerInvariant(), "The plan should list what is live but missing from the YAML as 'would remove'.");
        Expect.Contains("tn_fax", output, "The plan should name the field it would remove.");
        var written = Written(client);
        Expect.Equal(1, CountOfControl(written, "tn_fax"), "Without --allow-removals the deleted field must stay on the form.");
        Expect.Equal("Email address", (string?)CellOf(written, "tn_email").Element("labels")!.Element("label")!.Attribute("description"), "The rename in the same YAML should still be imported.");
    }

    // Example: the same YAML with --allow-removals -> tn_fax is removed from the form that is written.
    [Fact]
    public async Task FormImport_FieldDeletedFromTheYamlWithAllowRemovals_RemovesItFromTheForm()
    {
        var (exitCode, output, client) = await ImportAsync(FaxDeletedAndEmailRenamed(), "--allow-removals");

        Expect.Equal(0, exitCode, $"The import should succeed. Output: <{output}>");
        Expect.Equal(0, CountOfControl(Written(client), "tn_fax"), "With --allow-removals the deleted field should be removed from the form.");
    }

    // ---- moving a field is not a removal ----

    // Example: tn_fax moves from "details" to "notes_section" -> no "would remove", no flag needed, and it ends up in the new section.
    [Fact]
    public async Task FormImport_FieldMovedToAnotherSection_NeedsNoFlagAndListsNothingToRemove()
    {
        var fax = ModelOf(SmallLive()).FindControl("tn_fax");
        var yaml = ModelOf(SmallLive())
            .EditSection("general", "details", s => s.EditControls(c => c.Where(x => x.Id != "tn_fax").ToList()))
            .EditSection("general", "notes_section", s => s.EditControls(c => [.. c, fax]));

        var (exitCode, output, client) = await ImportAsync(yaml);

        Expect.Equal(0, exitCode, $"A move should import without any flag. Output: <{output}>");
        Expect.True(!output.Contains("would remove", StringComparison.OrdinalIgnoreCase), $"A move is not a removal, so nothing should be listed as 'would remove'. Output: <{output}>");
        var written = Written(client);
        Expect.Equal(1, CountOfControl(written, "tn_fax"), "The moved field should be on the form once.");
        Expect.True(SectionOf(written, "general", "notes_section").Descendants("control").Any(c => (string?)c.Attribute("id") == "tn_fax"), "tn_fax should be in notes_section.");
    }

    // ---- the flags exist ----

    // Example: `form import --help` and `solution import --help` both offer --allow-removals and --allow-unexpected.
    [Theory]
    [InlineData("form", "--allow-removals")]
    [InlineData("form", "--allow-unexpected")]
    [InlineData("solution", "--allow-removals")]
    [InlineData("solution", "--allow-unexpected")]
    public async Task ImportCommand_OffersTheSafetyFlags(string branch, string option)
    {
        using var console = new ConsoleHarness();

        var exitCode = await FormCliHost.SignedIn().RunAsync(console, branch, "import", "--help");

        Expect.Equal(0, exitCode, $"'{branch} import --help' should succeed. Output: <{console.Output}>");
        Expect.Contains(option, console.Output, $"'{branch} import' should offer {option}.");
    }
}

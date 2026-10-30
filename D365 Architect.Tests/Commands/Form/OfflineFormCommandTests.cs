using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Commands.Form;

/// <summary>
/// <c>form export --from-xml</c> and <c>form build-xml --base</c> (issue #59, part 0): the form round trip as a pure
/// file operation, so it runs locally and in CI with no sign-in. Driven through the real command tree
/// (<see cref="FormCliHost"/>) with a sign-in that fails the test if anything tries it.
/// Code path: <c>ExportFormCommand</c> and <c>BuildFormXmlCommand</c> (<c>D365 Architect/Commands/Form</c>).
/// In the "Console" collection because the command tree writes to <c>AnsiConsole.Console</c>.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class OfflineFormCommandTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static readonly string AccountFixture = FixturePath("account-main.xml");

    private string[] ExportedYamlFiles() => Directory.GetFiles(_directory.Path, "*.form.yml");

    private static async Task<(int ExitCode, NoSignInAuthentication Authentication, string Output)> ExportAsync(params string[] args)
    {
        using var console = new ConsoleHarness();
        var (host, authentication) = FormCliHost.NotSignedIn();
        var exitCode = await host.RunAsync(console, ["form", "export", .. args]);
        return (exitCode, authentication, console.Output);
    }

    private async Task<FormDefinition> ExportAccountFixtureAsync()
    {
        var (exitCode, _, output) = await ExportAsync("--from-xml", AccountFixture, "--table", "account", "-o", _directory.Path);
        Expect.Equal(0, exitCode, $"Exporting the fixture should succeed. Output: <{output}>");
        return FormYamlDeserializer.FromYaml(File.ReadAllText(Assert.Single(ExportedYamlFiles())));
    }

    // ---- form export --from-xml ----

    // Example: `form export --from-xml account.xml --table account -o out` writes a *.form.yml and never signs in.
    [Fact]
    public async Task FormExportFromXml_LocalSolutionExportFile_WritesTheYamlWithoutSigningIn()
    {
        var (exitCode, authentication, output) = await ExportAsync("--from-xml", AccountFixture, "--table", "account", "-o", _directory.Path);

        Expect.Equal(0, exitCode, $"form export --from-xml should succeed. Output: <{output}>");
        Expect.Equal(0, authentication.Attempts, "form export --from-xml must not sign in.");
        Expect.Equal(1, ExportedYamlFiles().Length, $"Exactly one *.form.yml should be written to the output directory. Files: <{string.Join(", ", Directory.GetFiles(_directory.Path))}>.");
    }

    // Example: the solution file's <formid> and <LocalizedName> become the YAML's formId and form name; --table is the entity.
    [Fact]
    public async Task FormExportFromXml_SolutionExportFile_TakesTheFormIdentityFromTheFile()
    {
        var form = await ExportAccountFixtureAsync();

        Expect.Equal(AccountFormName, form.Name, "The form name should come from the solution file's <LocalizedName>.");
        Expect.Equal(new Guid("3cda23b8-4ad0-a856-5f17-0e809b5f7201"), form.FormId ?? Guid.Empty, "The formId should come from the solution file's <formid>.");
        Expect.Equal("account", form.Entity, "The entity should be the --table.");
        Expect.True(form.Type is null or "Main", $"A form exported without --type is a Main form. Actual type: <{form.Type}>.");
    }

    // Example: the same FormXML read by the live export (FormJsonDefinitionReader) and by --from-xml gives the same YAML.
    [Fact]
    public async Task FormExportFromXml_SolutionExportFile_ExportsTheSameModelFormExportWouldFromDataverse()
    {
        var form = await ExportAccountFixtureAsync();

        // Only the FormXML content is compared: the record-level fields (description, activation state, ...) are not in the FormXML.
        static string Content(FormDefinition f) => FormYamlSerializer.ToYaml(f
            .With(nameof(FormDefinition.Description), null).With(nameof(FormDefinition.IsDefault), null)
            .With(nameof(FormDefinition.FormActivationState), null).With(nameof(FormDefinition.IsCustomizable), null));

        Expect.Equal(AccountModel().Tabs.Count, form.Tabs.Count, "Every tab of the file should be exported.");
        Expect.Equal(Content(AccountModel()), Content(form), "The YAML should be what the live export would write for the same FormXML.");
    }

    // Example: a bare <form> file with --type "Quick View Form" -> the YAML's type is Quick View Form.
    [Fact]
    public async Task FormExportFromXml_BareFormFileWithType_WritesThatFormType()
    {
        var path = _directory.File("bare.xml");
        File.WriteAllText(path, SmallLive().ToString(SaveOptions.DisableFormatting));

        var (exitCode, authentication, output) = await ExportAsync("--from-xml", path, "--table", "tn_test", "--type", "Quick View Form", "-o", _directory.Path);

        Expect.Equal(0, exitCode, $"form export --from-xml on a bare form should succeed. Output: <{output}>");
        Expect.Equal(0, authentication.Attempts, "form export --from-xml must not sign in.");
        var form = FormYamlDeserializer.FromYaml(File.ReadAllText(Assert.Single(ExportedYamlFiles())));
        Expect.Equal("Quick View Form", form.Type, "--type should set the exported form's type.");
        Expect.Equal("tn_test", form.Entity, "The entity should be the --table.");
    }

    // ---- form build-xml --base ----

    private string WriteYaml(FormDefinition form)
    {
        var path = _directory.File("input.form.yml");
        File.WriteAllText(path, FormYamlSerializer.ToYaml(form));
        return path;
    }

    private static async Task<(int ExitCode, NoSignInAuthentication Authentication, string Output)> BuildAsync(string yamlPath, string basePath, string outputPath)
    {
        using var console = new ConsoleHarness();
        var (host, authentication) = FormCliHost.NotSignedIn();
        var exitCode = await host.RunAsync(console, "form", "build-xml", "--input", yamlPath, "--base", basePath, "-o", outputPath);
        return (exitCode, authentication, console.Output);
    }

    // Example: `form build-xml --input account.form.yml --base account.xml -o out.xml` writes out.xml and never signs in.
    [Fact]
    public async Task FormBuildXml_WithALocalBaseFile_WritesTheXmlWithoutSigningIn()
    {
        var output = _directory.File("out.xml");

        var (exitCode, authentication, text) = await BuildAsync(WriteYaml(AccountModel()), AccountFixture, output);

        Expect.Equal(0, exitCode, $"form build-xml --base should succeed. Output: <{text}>");
        Expect.Equal(0, authentication.Attempts, "form build-xml --base must not sign in.");
        Expect.True(File.Exists(output), $"The rebuilt FormXML should be written to <{output}>.");
    }

    // Example: the fixture's form attributes and <Navigation>, which the YAML never carries, are still in the output because the base file was patched.
    [Fact]
    public async Task FormBuildXml_WithALocalBaseFile_PatchesOntoTheBaseRatherThanBuildingFromScratch()
    {
        var output = _directory.File("out.xml");

        await BuildAsync(WriteYaml(AccountModel()), AccountFixture, output);

        var form = FormXmlFile.LoadForm(output);
        Expect.Equal("HighWithControls", (string?)form.Attribute("headerdensity"), "The base form's own attributes should survive the build.");
        Expect.True(form.Element("Navigation") is not null, "The base form's <Navigation>, which the YAML never carries, should survive the build.");
    }

    // Example: the base is a solution export, so the output keeps <forms><systemform><formid>...</formid>...</systemform></forms> around the new <form>.
    [Fact]
    public async Task FormBuildXml_SolutionExportBase_KeepsTheWrapperAroundTheForm()
    {
        var output = _directory.File("out.xml");

        await BuildAsync(WriteYaml(AccountModel()), AccountFixture, output);

        var document = XDocument.Load(output);
        Expect.Equal("forms", document.Root!.Name.LocalName, "The output should keep the solution export's <forms> wrapper.");
        Expect.Equal("{3cda23b8-4ad0-a856-5f17-0e809b5f7201}", (string?)document.Root.Element("systemform")?.Element("formid"), "The wrapper's <formid> should be unchanged.");
        Expect.Equal(AccountFormName, (string?)document.Root.Element("systemform")?.Element("LocalizedNames")?.Element("LocalizedName")?.Attribute("description"), "The wrapper's <LocalizedNames> should be unchanged.");
    }

    [Fact]
    public async Task FormBuildXml_BareFormBase_WritesABareForm()
    {
        var basePath = _directory.File("bare.xml");
        File.WriteAllText(basePath, SmallLive().ToString(SaveOptions.DisableFormatting));
        var output = _directory.File("out.xml");

        var (exitCode, _, text) = await BuildAsync(WriteYaml(ModelOf(SmallLive())), basePath, output);

        Expect.Equal(0, exitCode, $"form build-xml --base on a bare form should succeed. Output: <{text}>");
        Expect.Equal("form", XDocument.Load(output).Root!.Name.LocalName, "A bare base file should give a bare <form> back.");
    }

    // ---- the whole offline round trip (#58/#59 acceptance) ----

    // Example: account fixture -> export --from-xml -> build-xml --base (same file) -> compare reports 0 differences.
    [Fact]
    public async Task OfflineRoundTrip_ExportThenBuildOnTheAccountFixture_ReportsNoDifferences()
    {
        await ExportAccountFixtureAsync();
        var output = _directory.File("out.xml");

        var (exitCode, _, text) = await BuildAsync(Assert.Single(ExportedYamlFiles()), AccountFixture, output);

        Expect.Equal(0, exitCode, $"form build-xml --base should succeed. Output: <{text}>");
        var differences = FormXmlComparer.Compare(AccountMain(), FormXmlFile.LoadForm(output));
        Expect.Empty(differences, $"An unedited export then build must reproduce the fixture exactly. {Show(differences)}");
    }

    // Example: the exported YAML plus one new "Invoice Centres" tab with a subgrid -> every difference is inside that tab.
    [Fact]
    public async Task OfflineRoundTrip_AfterAddingATabToTheYaml_ChangesOnlyThatTab()
    {
        var form = (await ExportAccountFixtureAsync()).WithTabAppended(InvoiceCentresTab());
        var output = _directory.File("out.xml");

        await BuildAsync(WriteYaml(form), AccountFixture, output);

        var differences = FormXmlComparer.Compare(AccountMain(), FormXmlFile.LoadForm(output));
        Expect.True(differences.Count > 0, "The new tab should appear in the output.");
        Expect.True(differences.All(d => d.Path.Contains("tab[name=invoice_centres]", StringComparison.Ordinal)), $"Every difference should be inside the new tab. {Show(differences)}");
    }
}

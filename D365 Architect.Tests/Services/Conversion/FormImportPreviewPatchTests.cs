using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #58: the import preview must compare the new FormXML against the live form exactly as Dataverse returns it.
/// It used to rebuild the live form through the same lossy writer first
/// (<c>FormImportService.PreviewAsync</c>'s <c>ExistingComparableFormXml</c>), so anything the architect would
/// lose was lost on both sides and never showed in the diff - which is how #58 went unnoticed.
/// Code path: <see cref="FormImportService.PreviewAsync"/> and <see cref="FormImportPreview"/>, against a
/// <see cref="FakeDataverseClient"/>.
/// </summary>
public sealed class FormImportPreviewPatchTests
{
    private static readonly Uri Environment = new("https://test.crm.dynamics.com");

    private static Task<FormImportPreview> PreviewAsync(XElement live, FormDefinition yaml)
    {
        var client = new FakeDataverseClient
        {
            SystemForm = new ExistingSystemForm(yaml.FormId ?? Guid.Empty, live.ToString(SaveOptions.DisableFormatting), yaml.Entity, yaml.Name),
        };

        return new FormImportService(client, new FormJsonDefinitionReader()).PreviewAsync(Environment, "token", yaml, CancellationToken.None);
    }

    // Example: re-importing the unedited YAML of the Account fixture shows an empty diff, whatever the fixture carries that the YAML does not.
    [Fact]
    public async Task PreviewAsync_UneditedYamlOnTheAccountFixture_ShowsNoChanges()
    {
        var live = AccountMain();

        var preview = await PreviewAsync(live, ThroughYaml(AccountModel()));

        Expect.True(!preview.HasChanges, "Re-importing an unedited YAML should show an empty diff.");
        var againstLive = FormXmlComparer.Compare(live, XElement.Parse(preview.NewFormXml));
        Expect.Empty(againstLive, $"The new FormXML should be the live form itself, so a lossy rebuild could not hide behind an equally lossy live side. {Show(againstLive)}");
        var comparableSide = FormXmlComparer.Compare(live, XElement.Parse(preview.ExistingComparableFormXml));
        Expect.Empty(comparableSide, $"The side the new FormXML is compared against should be the live form, not a rebuild of it. {Show(comparableSide)}");
    }

    // Example: the live side of the diff is the live form as returned, spacers and section label settings included, not a rebuild of it.
    [Fact]
    public async Task PreviewAsync_LiveFormWithSpacersAndLabelSettings_ComparesAgainstTheLiveFormExactlyAsReturned()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_name", c => c.With(nameof(FormControl.Label), "Full name"));

        var preview = await PreviewAsync(live, yaml);

        var differences = FormXmlComparer.Compare(XElement.Parse(preview.ExistingFormXml), XElement.Parse(preview.ExistingComparableFormXml));
        Expect.Empty(differences, $"The side compared against must be the live FormXML itself, with nothing normalised away (spacer cells, labelwidth, locklevel, application, ids). {Show(differences)}");
    }

    // Example: one renamed label -> the diff between the live side and the new FormXML is that one label, nothing else.
    [Fact]
    public async Task PreviewAsync_OneLabelRenamed_TheDiffIsThatLabelAndNothingElse()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_name", c => c.With(nameof(FormControl.Label), "Full name"));

        var preview = await PreviewAsync(live, yaml);

        Expect.True(preview.HasChanges, "A renamed label is a change.");
        var differences = FormXmlComparer.Compare(live, XElement.Parse(preview.NewFormXml));
        var difference = Assert.Single(differences);
        Expect.True(difference.Path.EndsWith("/@description", StringComparison.Ordinal) && difference.After == "Full name", $"The only difference from the live form should be the new label text. {Show(differences)}");
        var shown = FormXmlComparer.Compare(XElement.Parse(preview.ExistingComparableFormXml), XElement.Parse(preview.NewFormXml));
        Expect.Equal(1, shown.Count, $"The diff the user sees should be that one label as well. {Show(shown)}");
    }
}

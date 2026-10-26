using System.Text.Json;
using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="FormImportService.PreviewAsync"/> against a
/// <see cref="FakeDataverseClient"/> - no real HTTP call ever happens.
/// Issue #33 (sub-issue of #30, "Solution import fixes"): a form import whose
/// rebuilt FormXML matches what's live must not be blocked by schema
/// violations, since there is nothing to import and so nothing to validate.
/// </summary>
public sealed class FormImportServiceTests
{
    private static readonly Uri Environment = new("https://test.crm.dynamics.com");
    private static readonly Guid FormId = new("11111111-2222-3333-4444-555555555555");

    private static FormDefinition SimpleForm(string label) => new()
    {
        Name = "Contact Quick Create",
        Entity = "contact",
        FormId = FormId,
        Tabs =
        [
            new FormTab
            {
                Name = "tab_1",
                Label = "General",
                Columns =
                [
                    new FormColumn
                    {
                        Width = "100%",
                        Sections =
                        [
                            new FormSection
                            {
                                Name = "section_1",
                                Controls = [new FormControl { Id = "firstname", Field = "firstname", Label = label, Control = "SingleLineText" }],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>
    /// A live FormXML that this tool can rebuild byte-for-byte, but which
    /// carries one attribute the vendored FormXML schema doesn't declare and
    /// that <see cref="FormXmlValidationMessage.IsKnownHarmless"/> does not
    /// allowlist - so <see cref="FormXmlValidator"/> reports a blocking
    /// violation against it. This tool never touches the attribute (it
    /// survives the rebuild verbatim, the way real-world attributes such as
    /// headerdensity do), which is exactly the issue #33 situation: a
    /// no-op rebuild of an already-live form that the validator nonetheless
    /// refuses.
    /// </summary>
    private static string LiveFormXmlWithAnUnrecognisedAttribute()
    {
        var root = XElement.Parse(FormXmlWriter.Write(SimpleForm("First Name")));
        root.SetAttributeValue("someunrecognisedattribute", "x");
        return root.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>The form as `form export` would have written it for the live FormXML, i.e. what a human re-imports unedited.</summary>
    private static FormDefinition ExportedFormFor(string liveFormXml)
    {
        var json = JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new { name = "Contact Quick Create", objecttypecode = "contact", formid = FormId, type = 7, formxml = liveFormXml },
            },
        });

        return new FormJsonDefinitionReader().Read(json)[0];
    }

    private static FormImportService CreateService(string liveFormXml)
    {
        var client = new FakeDataverseClient { SystemForm = new ExistingSystemForm(FormId, liveFormXml, "contact", "Contact Quick Create") };
        return new FormImportService(client, new FormJsonDefinitionReader());
    }

    // Given a form whose rebuilt FormXML is identical to the live one but
    // whose live FormXML would fail schema validation, when
    // FormImportService.PreviewAsync runs, then HasChanges is false and
    // Violations is empty, so the import is not blocked.
    [Fact]
    public async Task PreviewAsync_RebuiltFormXmlMatchesLive_ReportsNoViolations()
    {
        var liveFormXml = LiveFormXmlWithAnUnrecognisedAttribute();
        var service = CreateService(liveFormXml);
        var form = ExportedFormFor(liveFormXml);

        var preview = await service.PreviewAsync(Environment, "token", form, CancellationToken.None);

        Assert.False(preview.HasChanges,
            "Precondition: re-importing the unedited export of a live form should be an empty diff (\"No changes\").");
        Assert.True(preview.Violations.Count == 0,
            "expected no violations when the rebuilt FormXML matches live (nothing to import, so nothing to validate), but got: "
            + string.Join(" | ", preview.Violations.Select(v => v.Message)));
    }

    // A no-op only skips the FormXML *schema* validator. A control
    // with both 'control' and 'customControlId' renders identically to one
    // with just 'control' (Control wins), so the diff is empty, but
    // FormControlValidator must still report the conflict.
    [Fact]
    public async Task PreviewAsync_NoOpButControlAndCustomControlIdBothSet_StillReportsTheConflict()
    {
        var liveFormXml = FormXmlWriter.Write(SimpleForm("First Name"));
        var service = CreateService(liveFormXml);
        var form = new FormDefinition
        {
            Name = "Contact Quick Create",
            Entity = "contact",
            FormId = FormId,
            Tabs =
            [
                new FormTab
                {
                    Name = "tab_1",
                    Label = "General",
                    Columns =
                    [
                        new FormColumn
                        {
                            Width = "100%",
                            Sections =
                            [
                                new FormSection
                                {
                                    Name = "section_1",
                                    Controls = [new FormControl { Id = "firstname", Field = "firstname", Label = "First Name", Control = "SingleLineText", CustomControlId = "{11111111-2222-3333-4444-555555555555}" }],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var preview = await service.PreviewAsync(Environment, "token", form, CancellationToken.None);

        Assert.False(preview.HasChanges, "Precondition: Control wins over CustomControlId, so the rebuilt FormXML matches live.");
        Assert.Contains(preview.Violations, v => !v.IsKnownHarmless && v.Message.Contains("both 'control'", StringComparison.Ordinal));
    }

    // Boundary: the validator must still run when there IS a change,
    // so that skipping it for no-op rebuilds doesn't switch validation off
    // for real imports. Given the same live form but an edited label, the
    // unrecognised attribute is still reported as a blocking violation.
    [Fact]
    public async Task PreviewAsync_RebuiltFormXmlDiffersFromLive_StillReportsBlockingViolations()
    {
        var liveFormXml = LiveFormXmlWithAnUnrecognisedAttribute();
        var service = CreateService(liveFormXml);
        var edited = ExportedFormFor(liveFormXml);
        // Same form, one label edited, so the rebuild now differs from live.
        var editedForm = new FormDefinition
        {
            Name = edited.Name,
            Entity = edited.Entity,
            FormId = edited.FormId,
            Type = edited.Type,
            Tabs = SimpleForm("Given Name").Tabs,
        };

        var preview = await service.PreviewAsync(Environment, "token", editedForm, CancellationToken.None);

        Assert.True(preview.HasChanges, "Precondition: the edited label should make the rebuilt FormXML differ from live.");
        Assert.Contains(preview.Violations, v => !v.IsKnownHarmless && v.Message.Contains("'someunrecognisedattribute'", StringComparison.Ordinal));
    }
}

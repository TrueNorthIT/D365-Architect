using System.Text.Json;
using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="FormXmlWriter"/> and its round-trip with
/// <see cref="FormJsonDefinitionReader"/> - the most complex, and (before this
/// suite) entirely untested, conversion path in the codebase.
/// </summary>
public sealed class FormXmlWriterTests
{
    private static FormDefinition SimpleForm(string label = "Name") => new()
    {
        Name = "Test Form",
        Entity = "tn_test",
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
                                Controls = [new FormControl { Id = "tn_name", Field = "tn_name", Label = label, Control = "SingleLineText" }],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("InteractionCentricDashboard")]
    [InlineData("Contextual Dashboard")]
    [InlineData("Power BI Dashboard")]
    public void Write_DashboardType_ThrowsNotSupported(string type)
    {
        var form = new FormDefinition { Name = "A Dashboard", Entity = "tn_test", Type = type };
        Assert.Throws<NotSupportedException>(() => FormXmlWriter.Write(form));
    }

    [Fact]
    public void Write_FromScratch_ProducesTabsColumnsSectionsAndControl()
    {
        var xml = FormXmlWriter.Write(SimpleForm());
        var root = XElement.Parse(xml);

        var control = root.Element("tabs")!.Element("tab")!.Element("columns")!.Element("column")!
            .Element("sections")!.Element("section")!.Element("rows")!.Element("row")!.Element("cell")!.Element("control")!;

        Assert.Equal("tn_name", (string)control.Attribute("id")!);
        Assert.Equal("tn_name", (string)control.Attribute("datafieldname")!);
        Assert.Equal("{4273EDBD-AC1D-40D3-9FB2-095C621B552D}", (string)control.Attribute("classid")!); // SingleLineText
    }

    [Fact]
    public void Write_IsDeterministic_SameYamlProducesByteIdenticalXmlEveryTime()
    {
        var first = FormXmlWriter.Write(SimpleForm());
        var second = FormXmlWriter.Write(SimpleForm());
        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_DifferentTabName_ProducesADifferentDeterministicId()
    {
        var formA = SimpleForm();
        var formB = new FormDefinition { Name = "Test Form", Entity = "tn_test", Tabs = [new FormTab { Name = "tab_2", Columns = [] }] };

        var idA = XElement.Parse(FormXmlWriter.Write(formA)).Element("tabs")!.Element("tab")!.Attribute("id")!.Value;
        var idB = XElement.Parse(FormXmlWriter.Write(formB)).Element("tabs")!.Element("tab")!.Attribute("id")!.Value;

        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public void Write_PatchMode_PreservesElementsThisToolNeverManages()
    {
        var existing = XElement.Parse("""
            <form showImage="true">
              <Navigation><NavBar Area="Details" /></Navigation>
              <tabs><tab id="{old}"><columns /></tab></tabs>
              <clientresources><Library name="old.js" /></clientresources>
            </form>
            """);

        var xml = FormXmlWriter.Write(SimpleForm(), existing);
        var root = XElement.Parse(xml);

        // Untouched, never-decomposed elements survive verbatim.
        Assert.NotNull(root.Element("Navigation"));
        Assert.NotNull(root.Element("clientresources"));
        Assert.Equal("true", (string)root.Attribute("showImage")!);
        // The managed <tabs> element, however, was replaced with the new content.
        Assert.Equal("tab_1", (string)root.Element("tabs")!.Element("tab")!.Attribute("name")!);
    }

    [Fact]
    public void Write_PatchMode_RemovesManagedElementNoLongerPresentInYaml()
    {
        var existing = XElement.Parse("""
            <form>
              <ancestor id="{11111111-1111-1111-1111-111111111111}" />
              <tabs />
            </form>
            """);

        // form.Ancestor is null - the existing <ancestor> element must be removed, not left stale.
        var xml = FormXmlWriter.Write(SimpleForm(), existing);
        Assert.Null(XElement.Parse(xml).Element("ancestor"));
    }

    [Fact]
    public void Write_HiddenFields_ProducesHiddencontrolsElement()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            HiddenFields = [new FormHiddenField { Field = "owningteam" }],
        };

        var root = XElement.Parse(FormXmlWriter.Write(form));
        var data = root.Element("hiddencontrols")!.Element("data")!;
        Assert.Equal("owningteam", (string)data.Attribute("id")!);
        Assert.Equal("owningteam", (string)data.Attribute("datafieldname")!);
    }

    [Fact]
    public void Write_DisplayCondition_WithNoRoles_WritesEveryone()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            DisplayCondition = new FormDisplayCondition { FallbackForm = true, Order = 0 },
        };

        var root = XElement.Parse(FormXmlWriter.Write(form));
        var conditions = root.Element("DisplayConditions")!;
        Assert.NotNull(conditions.Element("Everyone"));
        Assert.Equal("true", (string)conditions.Attribute("FallbackForm")!);
    }

    // ---- Round-trip through FormJsonDefinitionReader ----

    [Fact]
    public void RoundTrip_WriteThenRead_PreservesCoreStructure()
    {
        var original = SimpleForm(label: "Full Name");
        var formXml = FormXmlWriter.Write(original);

        var wrapped = JsonSerializer.Serialize(new
        {
            value = new[] { new { name = original.Name, formid = Guid.Empty, objecttypecode = original.Entity, formxml = formXml } },
        });

        var reader = new FormJsonDefinitionReader();
        var readBack = reader.Read(wrapped)[0];

        Assert.Equal("Test Form", readBack.Name);
        Assert.Equal("tn_test", readBack.Entity);
        var tab = Assert.Single(readBack.Tabs);
        Assert.Equal("tab_1", tab.Name);
        Assert.Equal("General", tab.Label);
        var control = Assert.Single(tab.Columns[0].Sections[0].Controls);
        Assert.Equal("tn_name", control.Field);
        Assert.Equal("Full Name", control.Label);
        Assert.Equal("SingleLineText", control.Control);
    }

    [Fact]
    public void RoundTrip_RebuildingTheSameYamlTwice_ProducesIdenticalFormXml()
    {
        // The whole point of DeterministicGuid: re-running import on
        // unmodified YAML must never show a spurious diff.
        var form = SimpleForm();
        var first = FormXmlWriter.Write(form);
        var second = FormXmlWriter.Write(form);
        Assert.Equal(first, second);
    }
}

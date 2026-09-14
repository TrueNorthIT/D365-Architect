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
    public void Write_HiddenField_WithClassIdAndRelationship_WritesBothAttributes()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            HiddenFields = [new FormHiddenField { Field = "owninguser", ClassId = "{some-classid}", Relationship = "lead_owning_user" }],
        };

        var data = XElement.Parse(FormXmlWriter.Write(form)).Element("hiddencontrols")!.Element("data")!;
        Assert.Equal("{some-classid}", (string)data.Attribute("classid")!);
        Assert.Equal("lead_owning_user", (string)data.Attribute("relationship")!);
    }

    // ---- Fields silently dropped on export/import round-trip until fe72321 ----

    [Fact]
    public void Write_HiddenTabAndSection_WriteVisibleFalse()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs =
            [
                new FormTab
                {
                    Name = "tab_1",
                    Visible = false,
                    Columns = [new FormColumn { Sections = [new FormSection { Name = "section_1", Visible = false }] }],
                },
            ],
        };

        var root = XElement.Parse(FormXmlWriter.Write(form));
        var tab = root.Element("tabs")!.Element("tab")!;
        var section = tab.Element("columns")!.Element("column")!.Element("sections")!.Element("section")!;

        Assert.Equal("false", (string)tab.Attribute("visible")!);
        Assert.Equal("false", (string)section.Attribute("visible")!);
    }

    [Fact]
    public void Write_HiddenControlLabelAndSectionLabel_WriteShowLabelFalse()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs =
            [
                new FormTab
                {
                    Name = "tab_1",
                    Columns =
                    [
                        new FormColumn
                        {
                            Sections =
                            [
                                new FormSection
                                {
                                    Name = "section_1",
                                    ShowLabel = false,
                                    Controls = [new FormControl { Id = "tn_name", Field = "tn_name", ShowLabel = false, Control = "SingleLineText" }],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var root = XElement.Parse(FormXmlWriter.Write(form));
        var section = root.Element("tabs")!.Element("tab")!.Element("columns")!.Element("column")!.Element("sections")!.Element("section")!;
        var cell = section.Element("rows")!.Element("row")!.Element("cell")!;

        Assert.Equal("false", (string)section.Attribute("showlabel")!);
        Assert.Equal("false", (string)cell.Attribute("showlabel")!);
    }

    [Fact]
    public void Write_TabCollapsibleAndAvailableOnPhone_WritesBothAttributes()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs = [new FormTab { Name = "tab_1", Collapsible = true, AvailableOnPhone = false, Columns = [] }],
        };

        var tab = XElement.Parse(FormXmlWriter.Write(form)).Element("tabs")!.Element("tab")!;
        Assert.Equal("true", (string)tab.Attribute("collapsible")!);
        Assert.Equal("false", (string)tab.Attribute("availableforphone")!);
    }

    [Fact]
    public void Write_ControlIsUnboundAndIsRequired_WritesBothAttributes()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs =
            [
                new FormTab
                {
                    Name = "tab_1",
                    Columns =
                    [
                        new FormColumn
                        {
                            Sections =
                            [
                                new FormSection
                                {
                                    Name = "section_1",
                                    Controls = [new FormControl { Id = "tn_name", Field = "tn_name", Control = "SingleLineText", IsUnbound = true, IsRequired = true }],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var control = XElement.Parse(FormXmlWriter.Write(form)).Element("tabs")!.Element("tab")!.Element("columns")!.Element("column")!
            .Element("sections")!.Element("section")!.Element("rows")!.Element("row")!.Element("cell")!.Element("control")!;

        Assert.Equal("true", (string)control.Attribute("isunbound")!);
        Assert.Equal("true", (string)control.Attribute("isrequired")!);
    }

    [Fact]
    public void Write_LabelTranslations_WritesOneAdditionalLabelElementPerLanguage()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs = [new FormTab { Name = "tab_1", Label = "General", Translations = new Dictionary<int, string> { [1036] = "Général" }, Columns = [] }],
        };

        var labels = XElement.Parse(FormXmlWriter.Write(form)).Element("tabs")!.Element("tab")!.Element("labels")!.Elements("label").ToList();

        Assert.Equal(2, labels.Count);
        Assert.Contains(labels, l => (string)l.Attribute("languagecode")! == "1033" && (string)l.Attribute("description")! == "General");
        Assert.Contains(labels, l => (string)l.Attribute("languagecode")! == "1036" && (string)l.Attribute("description")! == "Général");
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

    private static FormDefinition ReadBack(FormDefinition original)
    {
        var formXml = FormXmlWriter.Write(original);
        var wrapped = JsonSerializer.Serialize(new
        {
            value = new[] { new { name = original.Name, formid = Guid.Empty, objecttypecode = original.Entity, formxml = formXml } },
        });

        return new FormJsonDefinitionReader().Read(wrapped)[0];
    }

    [Fact]
    public void RoundTrip_PreviouslyDroppedTabAndSectionFields_AreAllPreserved()
    {
        // fe72321: Visible/ShowLabel/Translations/AvailableOnPhone/Collapsible
        // had no model property at all and were silently lost on every
        // export/import round-trip before that fix.
        var original = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs =
            [
                new FormTab
                {
                    Name = "tab_1",
                    Label = "General",
                    Translations = new Dictionary<int, string> { [1036] = "Général" },
                    Visible = false,
                    Collapsible = true,
                    AvailableOnPhone = false,
                    Columns =
                    [
                        new FormColumn
                        {
                            Sections =
                            [
                                new FormSection
                                {
                                    Name = "section_1",
                                    Visible = false,
                                    ShowLabel = false,
                                    AvailableOnPhone = true,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var tab = ReadBack(original).Tabs[0];
        var section = tab.Columns[0].Sections[0];

        Assert.False(tab.Visible);
        Assert.True(tab.Collapsible);
        Assert.False(tab.AvailableOnPhone);
        Assert.Equal("Général", tab.Translations![1036]);

        Assert.False(section.Visible);
        Assert.False(section.ShowLabel);
        Assert.True(section.AvailableOnPhone);
    }

    [Fact]
    public void RoundTrip_PreviouslyDroppedControlAndHiddenFieldFields_AreAllPreserved()
    {
        var original = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs =
            [
                new FormTab
                {
                    Name = "tab_1",
                    Columns =
                    [
                        new FormColumn
                        {
                            Sections =
                            [
                                new FormSection
                                {
                                    Name = "section_1",
                                    Controls =
                                    [
                                        new FormControl
                                        {
                                            Id = "tn_lookup",
                                            Field = "tn_lookup",
                                            Label = "Lookup Field",
                                            Control = "Lookup",
                                            Translations = new Dictionary<int, string> { [1036] = "Recherche" },
                                            IsUnbound = true,
                                            IsRequired = true,
                                            ShowLabel = false,
                                            AvailableOnPhone = true,
                                        },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
            HiddenFields = [new FormHiddenField { Field = "owninguser", Relationship = "lead_owning_user" }],
        };

        var readBack = ReadBack(original);
        var control = readBack.Tabs[0].Columns[0].Sections[0].Controls[0];
        var hiddenField = readBack.HiddenFields![0];

        Assert.True(control.IsUnbound);
        Assert.True(control.IsRequired);
        Assert.False(control.ShowLabel);
        Assert.True(control.AvailableOnPhone);
        Assert.Equal("Recherche", control.Translations![1036]);

        Assert.Equal("lead_owning_user", hiddenField.Relationship);
    }

    [Fact]
    public void RoundTrip_FalseInsideDataSetNode_IsPreserved_ButOrdinaryParameterFalse_IsStripped()
    {
        // 42ac565: "false" is dropped as equivalent to "omitted" for every
        // FormXml.xsd-governed parameter, but a <data-set>-wrapped block
        // belongs to a PCF control's own manifest instead, where a
        // boolean's structural presence (not just its value) can be
        // meaningful - confirmed live for ActivityCalendarControl's
        // IsUserView. Dropping it there breaks the import.
        var dataSet = new Dictionary<string, object>
        {
            ["attributes"] = new Dictionary<string, object> { ["name"] = "Calendar" },
            ["ViewId"] = "11111111-1111-1111-1111-111111111111",
            ["IsUserView"] = "false",
        };

        var original = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            Tabs =
            [
                new FormTab
                {
                    Name = "tab_1",
                    Columns =
                    [
                        new FormColumn
                        {
                            Sections =
                            [
                                new FormSection
                                {
                                    Name = "section_1",
                                    Controls =
                                    [
                                        new FormControl
                                        {
                                            Id = "tn_calendar",
                                            Control = "Subgrid",
                                            Parameters = new Dictionary<string, object> { ["data-set"] = dataSet },
                                        },
                                        new FormControl
                                        {
                                            Id = "tn_plain",
                                            Control = "Subgrid",
                                            Parameters = new Dictionary<string, object> { ["ShowChart"] = "false" },
                                        },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var controls = ReadBack(original).Tabs[0].Columns[0].Sections[0].Controls;

        var parameters = (IDictionary<string, object>)controls[0].Parameters!;
        var readBackDataSet = (IDictionary<string, object>)parameters["data-set"];
        Assert.Equal("false", readBackDataSet["IsUserView"]);

        // The sibling control's plain (non-data-set) "false" parameter is
        // dropped exactly as before - this fix must not weaken that rule
        // for the ordinary, XSD-governed case.
        Assert.Null(controls[1].Parameters);
    }
}

using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Forms and helpers for the patcher tests: the anonymised Account fixture (a real, heavily customised main form),
/// a small hand-written form with the same awkward features (spacer cells, an empty row, a multi-column section with
/// label settings, <c>locklevel</c>, a header and footer with layout, a system event), and a few ways of editing a
/// <see cref="FormDefinition"/> the way a person edits the YAML.
/// </summary>
internal static class FormPatchFixtures
{
    public const string AccountFormName = "Fixture form 1";

    public static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FormXml", fileName);

    /// <summary>The Account fixture's <c>&lt;form&gt;</c>, as <see cref="FormXmlFile.LoadForm"/> reads it from the solution export.</summary>
    public static XElement AccountMain() => FormXmlFile.LoadForm(FixturePath("account-main.xml"));

    // ---- the small form ----

    public const string SmallFormName = "Small Form";
    public static readonly Guid SmallFormId = new("0f0f0f0f-0000-4000-8000-000000000001");

    /// <summary>One cell; <paramref name="n"/> is two hex digits that make its ids unique. No control means a spacer.</summary>
    private static string Cell(string n, string? controlId, string label, int lockLevel = 0)
    {
        var id = $"{{A1B2C3D4-00{n}-4000-8000-0000000000{n}}}";
        return controlId is null
            ? $$"""<cell id="{{id}}" showlabel="false"><labels><label description="" languagecode="1033" /></labels></cell>"""
            : $$"""<cell id="{{id}}" showlabel="true" locklevel="{{lockLevel}}" labelid="{{id.Replace("A1B2C3D4", "E1E1E1E1")}}"><labels><label description="{{label}}" languagecode="1033" /></labels><control id="{{controlId}}" classid="{4273EDBD-AC1D-40d3-9FB2-095C621B552D}" datafieldname="{{controlId}}" disabled="false" uniqueid="{{id.Replace("A1B2C3D4", "B1B1B1B1")}}" /></cell>""";
    }

    /// <summary>
    /// A live form with what the old writer lost: in tab "general", section "details" (2 sub-columns, labelwidth 200,
    /// labels above) holds tn_name (locklevel 1) beside a spacer, a spacer beside tn_phone, an empty row, then tn_email and
    /// tn_fax; section "notes_section" holds tn_notes. Tab "address" holds tn_street. Header and footer have their own
    /// layout, and the system event carries application="true". Mixed-case GUIDs throughout. A fresh tree per call.
    /// </summary>
    public static XElement SmallLive() => XElement.Parse($$"""
        <form showImage="true" headerdensity="HighWithControls">
          <tabs>
            <tab name="general" id="{A1B2C3D4-0001-4000-8000-000000000001}" IsUserDefined="0" locklevel="0" showlabel="true" expanded="true" labelid="{a1b2c3d4-0001-4000-8000-0000000000f1}">
              <labels><label description="General" languagecode="1033" /></labels>
              <columns>
                <column width="100%">
                  <sections>
                    <section name="details" id="{A1B2C3D4-0002-4000-8000-000000000002}" IsUserDefined="0" locklevel="0" showlabel="true" showbar="true" layout="varheight" columns="11" labelwidth="200" celllabelposition="Top" celllabelalignment="Left" labelid="{a1b2c3d4-0002-4000-8000-0000000000f2}">
                      <labels><label description="Details" languagecode="1033" /></labels>
                      <rows>
                        <row>{{Cell("11", "tn_name", "Name", lockLevel: 1)}}{{Cell("12", null, "")}}</row>
                        <row>{{Cell("13", null, "")}}{{Cell("14", "tn_phone", "Phone")}}</row>
                        <row />
                        <row>{{Cell("15", "tn_email", "Email")}}{{Cell("16", "tn_fax", "Fax")}}</row>
                      </rows>
                    </section>
                    <section name="notes_section" id="{A1B2C3D4-0003-4000-8000-000000000003}" IsUserDefined="0" locklevel="0" showlabel="true" showbar="false" layout="varwidth" columns="1" labelwidth="115" celllabelposition="Left" celllabelalignment="Left" labelid="{a1b2c3d4-0003-4000-8000-0000000000f3}">
                      <labels><label description="Notes" languagecode="1033" /></labels>
                      <rows>
                        <row>{{Cell("17", "tn_notes", "Notes")}}</row>
                      </rows>
                    </section>
                  </sections>
                </column>
              </columns>
            </tab>
            <tab name="address" id="{a1b2c3d4-0004-4000-8000-000000000004}" IsUserDefined="0" locklevel="0" showlabel="true" labelid="{a1b2c3d4-0004-4000-8000-0000000000f4}">
              <labels><label description="Address" languagecode="1033" /></labels>
              <columns>
                <column width="100%">
                  <sections>
                    <section name="address_section" id="{a1b2c3d4-0005-4000-8000-000000000005}" IsUserDefined="0" locklevel="0" showlabel="true" showbar="false" layout="varwidth" columns="1" labelwidth="115" celllabelposition="Left" celllabelalignment="Left" labelid="{a1b2c3d4-0005-4000-8000-0000000000f5}">
                      <labels><label description="Address" languagecode="1033" /></labels>
                      <rows>
                        <row>{{Cell("18", "tn_street", "Street")}}</row>
                      </rows>
                    </section>
                  </sections>
                </column>
              </columns>
            </tab>
          </tabs>
          <header id="{A1B2C3D4-0006-4000-8000-000000000006}" columns="111" celllabelposition="Top" labelwidth="115">
            <rows>
              <row>{{Cell("19", "header_tn_owner", "Owner")}}{{Cell("1A", "header_tn_status", "Status")}}</row>
            </rows>
          </header>
          <footer id="{A1B2C3D4-0007-4000-8000-000000000007}" columns="111">
            <rows>
              <row>{{Cell("1B", "footer_tn_modified", "Modified")}}</row>
            </rows>
          </footer>
          <formLibraries>
            <Library name="tn_/lib.js" libraryUniqueId="{C1C1C1C1-0001-4000-8000-000000000001}" />
          </formLibraries>
          <events>
            <event name="onload" application="true" active="false">
              <InternalHandlers>
                <Handler functionName="Sys.onLoad" libraryName="sys/lib.js" handlerUniqueId="{D1D1D1D1-0001-4000-8000-000000000001}" enabled="true" />
              </InternalHandlers>
              <Handlers>
                <Handler functionName="Tn.onLoad" libraryName="tn_/lib.js" handlerUniqueId="{d1d1d1d1-0002-4000-8000-000000000002}" enabled="true" parameters="" passExecutionContext="true" />
              </Handlers>
            </event>
          </events>
        </form>
        """);

    // ---- reading a form into the model, as `form export` does ----

    /// <summary>The model <c>form export</c> would produce for <paramref name="form"/>: the real reader over a Dataverse-shaped response.</summary>
    public static FormDefinition ModelOf(XElement form, string name = SmallFormName, string entity = "tn_test", Guid? formId = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            value = new[] { new { name, objecttypecode = entity, formid = formId ?? SmallFormId, type = 2, formxml = form.ToString(SaveOptions.DisableFormatting) } },
        });

        return new FormJsonDefinitionReader().Read(json)[0];
    }

    public static FormDefinition AccountModel() => ModelOf(AccountMain(), AccountFormName, "account", new Guid("3cda23b8-4ad0-a856-5f17-0e809b5f7201"));

    /// <summary>The model after being written to a <c>*.form.yml</c> and read back, i.e. what an unedited re-import starts from.</summary>
    public static FormDefinition ThroughYaml(FormDefinition model) => FormYamlDeserializer.FromYaml(FormYamlSerializer.ToYaml(model));

    // ---- editing the model the way a person edits the YAML ----

    private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>A copy of <paramref name="source"/> with one property replaced (the model's properties are init-only).</summary>
    public static T With<T>(this T source, string property, object? value) where T : class
    {
        var copy = (T)CloneMethod.Invoke(source, null)!;
        typeof(T).GetProperty(property)!.SetValue(copy, value);
        return copy;
    }

    public static FormDefinition EditSection(this FormDefinition form, string tabName, string sectionName, Func<FormSection, FormSection> edit) =>
        form.With(nameof(FormDefinition.Tabs), form.Tabs.Select(tab => tab.Name != tabName
            ? tab
            : tab.With(nameof(FormTab.Columns), tab.Columns.Select(column =>
                column.With(nameof(FormColumn.Sections), column.Sections.Select(section => section.Name == sectionName ? edit(section) : section).ToList())).ToList())).ToList());

    public static FormSection EditControls(this FormSection section, Func<IReadOnlyList<FormControl>, IReadOnlyList<FormControl>> edit) =>
        section.With(nameof(FormSection.Controls), edit(section.Controls));

    /// <summary>Changes one body control wherever it sits.</summary>
    public static FormDefinition EditControl(this FormDefinition form, string controlId, Func<FormControl, FormControl> edit) =>
        form.EditEveryControlList(controls => controls.Select(control => control.Id == controlId ? edit(control) : control).ToList());

    /// <summary>Removes one body control from the YAML wherever it sits.</summary>
    public static FormDefinition WithoutControl(this FormDefinition form, string controlId) =>
        form.EditEveryControlList(controls => controls.Where(control => control.Id != controlId).ToList());

    private static FormDefinition EditEveryControlList(this FormDefinition form, Func<IReadOnlyList<FormControl>, IReadOnlyList<FormControl>> edit) =>
        form.With(nameof(FormDefinition.Tabs), form.Tabs.Select(tab =>
            tab.With(nameof(FormTab.Columns), tab.Columns.Select(column =>
                column.With(nameof(FormColumn.Sections), column.Sections.Select(section => section.EditControls(edit)).ToList())).ToList())).ToList());

    public static FormControl FindControl(this FormDefinition form, string controlId) =>
        form.Tabs.SelectMany(t => t.Columns).SelectMany(c => c.Sections).SelectMany(s => s.Controls).Single(c => c.Id == controlId);

    public static FormDefinition WithTabAppended(this FormDefinition form, FormTab tab) =>
        form.With(nameof(FormDefinition.Tabs), form.Tabs.Append(tab).ToList());

    public static FormDefinition WithoutTab(this FormDefinition form, string tabName) =>
        form.With(nameof(FormDefinition.Tabs), form.Tabs.Where(tab => tab.Name != tabName).ToList());

    /// <summary>A new tab "Invoice Centres" holding one subgrid, as the issue's example adds.</summary>
    public static FormTab InvoiceCentresTab() => new()
    {
        Name = "invoice_centres",
        Label = "Invoice Centres",
        Columns =
        [
            new FormColumn
            {
                Width = "100%",
                Sections =
                [
                    new FormSection
                    {
                        Name = "invoice_centres_section",
                        Label = "Invoice Centres",
                        Controls =
                        [
                            new FormControl
                            {
                                Id = "invoice_centres_grid",
                                Control = "Subgrid",
                                Parameters = new Dictionary<string, object> { ["TargetEntityType"] = "tn_invoicecentre", ["RelationshipName"] = "tn_account_invoicecentre" },
                            },
                        ],
                    },
                ],
            },
        ],
    };

    // ---- looking at FormXML ----

    public static XElement SectionOf(XElement form, string tabName, string sectionName) =>
        form.Element("tabs")!.Elements("tab").Single(t => (string?)t.Attribute("name") == tabName)
            .Descendants("section").Single(s => (string?)s.Attribute("name") == sectionName);

    public static XElement CellOf(XElement form, string controlId) =>
        form.Descendants("control").First(c => (string?)c.Attribute("id") == controlId).Parent!;

    public static int CountOfControl(XElement form, string controlId) =>
        form.Descendants("control").Count(c => (string?)c.Attribute("id") == controlId);

    /// <summary>
    /// Every cell of a section as "row.cell:controlId" (a spacer is "row.cell:spacer", an empty row has none), 1-based,
    /// so a test can say "every existing cell is still where it was".
    /// </summary>
    public static IReadOnlyList<string> GridOf(XElement section) =>
        section.Element("rows")!.Elements("row").SelectMany((row, r) =>
            row.Elements("cell").Select((cell, c) => $"{r + 1}.{c + 1}:{(string?)cell.Element("control")?.Attribute("id") ?? "spacer"}")).ToList();

    /// <summary>The differences as one line each, for assertion messages.</summary>
    public static string Show(IEnumerable<FormXmlDifference> differences, int max = 12)
    {
        var list = differences.ToList();
        var lines = list.Take(max).Select(d => $"{d.Kind} {d.Path} [{d.Before}] -> [{d.After}]");
        return $"{list.Count} difference(s): " + string.Join(" | ", lines) + (list.Count > max ? " | ..." : "");
    }
}

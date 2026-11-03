using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #58: before writing, <see cref="FormPatchVerifier.Verify"/> checks what a patch changed in the FormXML
/// against what the YAML asked for. <c>Expected</c> is every difference inside a tab, section or control whose model
/// differs between the live form's model and the YAML's (or that is new); everything else is <c>Unexpected</c> and
/// makes <c>form import</c> stop unless <c>--allow-unexpected</c> is passed (a dropped spacer, a regenerated id, a changed
/// header). <c>WouldRemove</c> lists what is live but missing from the YAML (and not simply moved), which stays unless
/// <c>--allow-removals</c> is passed. The "new form" XML in each test is edited by hand so the verifier is tested on its own,
/// not through the writer.
/// </summary>
public sealed class FormPatchVerifierTests
{
    private static FormPatchReport Verify(XElement live, XElement newForm, FormDefinition yaml) =>
        FormPatchVerifier.Verify(live, newForm, ModelOf(live), yaml);

    private static XElement Changed(Action<XElement> change)
    {
        var form = SmallLive();
        change(form);
        return form;
    }

    private static XElement Cell(XElement form, string cellIdSuffix) =>
        form.Descendants("cell").Single(c => ((string?)c.Attribute("id"))!.EndsWith(cellIdSuffix + "}", StringComparison.Ordinal));

    private static FormDefinition RenamedNameLabel() =>
        ModelOf(SmallLive()).EditControl("tn_name", c => c.With(nameof(FormControl.Label), "Full name"));

    private static string Describe(FormPatchReport report) =>
        $"Expected: {Show(report.Expected)}. Unexpected: {Show(report.Unexpected)}. WouldRemove: <{string.Join(", ", report.WouldRemove)}>.";

    // ---- what the YAML changed is expected ----

    // Example: the YAML renames tn_name's label and the new FormXML changes only that label -> Expected has it, nothing Unexpected.
    [Fact]
    public void Verify_OnlyTheLabelTheYamlChangedIsDifferent_ReportsItAsExpected()
    {
        var live = SmallLive();
        var newForm = Changed(f => CellOf(f, "tn_name").Element("labels")!.Element("label")!.SetAttributeValue("description", "Full name"));

        var report = Verify(live, newForm, RenamedNameLabel());

        Expect.Equal(1, report.Expected.Count, $"The renamed label is the one expected difference. {Describe(report)}");
        Expect.Empty(report.Unexpected, $"Nothing else changed. {Describe(report)}");
        Expect.Empty(report.WouldRemove, $"Nothing is missing from the YAML. {Describe(report)}");
    }

    // Example: the YAML adds a tab and the new FormXML has exactly that tab added -> Expected, nothing Unexpected.
    [Fact]
    public void Verify_NewTabAddedByTheYaml_ReportsItAsExpected()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).WithTabAppended(InvoiceCentresTab());
        var newForm = Changed(f => f.Element("tabs")!.Add(XElement.Parse("""
            <tab name="invoice_centres" id="{F1F1F1F1-0001-4000-8000-000000000001}"><labels><label description="Invoice Centres" languagecode="1033" /></labels>
              <columns><column width="100%"><sections><section name="invoice_centres_section" id="{F1F1F1F1-0002-4000-8000-000000000002}"><rows><row>
                <cell id="{F1F1F1F1-0003-4000-8000-000000000003}"><control id="invoice_centres_grid" classid="{E7A81278-8635-4d9e-8D4D-59480B391C5B}" /></cell>
              </row></rows></section></sections></column></columns></tab>
            """)));

        var report = Verify(live, newForm, yaml);

        Expect.True(report.Expected.Count > 0 && report.Expected.All(d => d.Path.Contains("tab[name=invoice_centres]", StringComparison.Ordinal)), $"The new tab is expected. {Describe(report)}");
        Expect.Empty(report.Unexpected, $"Nothing else changed. {Describe(report)}");
    }

    // ---- anything else is unexpected, and is named ----

    // Example: the YAML is unchanged, but the new FormXML lost a spacer cell -> Unexpected names it (path in section "details", cell 2).
    [Fact]
    public void Verify_SpacerCellDroppedFromAnUntouchedSection_ReportsItAsUnexpectedAndNamesIt()
    {
        var live = SmallLive();
        var newForm = Changed(f => Cell(f, "0012").Remove());

        var report = Verify(live, newForm, ModelOf(live));

        var difference = Assert.Single(report.Unexpected);
        Expect.Equal(FormXmlDifferenceKind.ElementRemoved, difference.Kind, $"The dropped spacer is a removed element. {Describe(report)}");
        Expect.Contains("section[name=details]", difference.Path, "The report should name the section the spacer was in.");
        Expect.True(difference.Path.EndsWith("/cell[2]", StringComparison.Ordinal), $"The report should name the spacer's position. Path: <{difference.Path}>.");
        Expect.Empty(report.Expected, $"The YAML changed nothing. {Describe(report)}");
    }

    [Fact]
    public void Verify_EmptyRowDroppedFromAnUntouchedSection_ReportsItAsUnexpected()
    {
        var live = SmallLive();
        var newForm = Changed(f => SectionOf(f, "general", "details").Element("rows")!.Elements("row").Single(r => !r.HasElements).Remove());

        var report = Verify(live, newForm, ModelOf(live));

        Expect.True(report.Unexpected.Any(d => d.Kind == FormXmlDifferenceKind.ElementRemoved && d.Path.Contains("section[name=details]", StringComparison.Ordinal)), $"The dropped empty row should be unexpected. {Describe(report)}");
    }

    [Fact]
    public void Verify_IdRegeneratedOnAnUntouchedTab_ReportsItAsUnexpected()
    {
        var live = SmallLive();
        var newForm = Changed(f => f.Element("tabs")!.Elements("tab").Single(t => (string?)t.Attribute("name") == "address").SetAttributeValue("id", "{00000000-0000-4000-8000-000000000000}"));

        var report = Verify(live, newForm, ModelOf(live));

        Expect.True(report.Unexpected.Any(d => d.Kind == FormXmlDifferenceKind.AttributeChanged && d.Path.EndsWith("/@id", StringComparison.Ordinal)), $"A regenerated id is unexpected. {Describe(report)}");
    }

    [Fact]
    public void Verify_SectionLabelSettingDroppedFromAnUntouchedSection_ReportsItAsUnexpected()
    {
        var live = SmallLive();
        var newForm = Changed(f => SectionOf(f, "general", "details").SetAttributeValue("labelwidth", null));

        var report = Verify(live, newForm, ModelOf(live));

        Expect.True(report.Unexpected.Any(d => d.Kind == FormXmlDifferenceKind.AttributeRemoved && d.Path.EndsWith("/@labelwidth", StringComparison.Ordinal)), $"A dropped labelwidth is unexpected. {Describe(report)}");
    }

    // Example: the YAML changes no header field, but the new FormXML lost the header's columns="111" -> Unexpected.
    [Fact]
    public void Verify_HeaderLayoutChangedWhenTheYamlLeavesTheHeaderAlone_ReportsItAsUnexpected()
    {
        var live = SmallLive();
        var newForm = Changed(f => f.Element("header")!.SetAttributeValue("columns", null));

        var report = Verify(live, newForm, RenamedNameLabel());

        Expect.True(report.Unexpected.Any(d => d.Path.StartsWith("header", StringComparison.Ordinal) && d.Path.EndsWith("/@columns", StringComparison.Ordinal)), $"A changed header is unexpected when the YAML did not change it. {Describe(report)}");
    }

    // Example: the YAML renames a label in "details"; the new FormXML also lost notes_section's labelwidth -> that part is Unexpected.
    [Fact]
    public void Verify_ChangeLeaksFromTheEditedSectionIntoAnother_ReportsOnlyTheLeakAsUnexpected()
    {
        var live = SmallLive();
        var newForm = Changed(f =>
        {
            CellOf(f, "tn_name").Element("labels")!.Element("label")!.SetAttributeValue("description", "Full name");
            SectionOf(f, "general", "notes_section").SetAttributeValue("labelwidth", null);
        });

        var report = Verify(live, newForm, RenamedNameLabel());

        Expect.True(report.Expected.Any(d => d.Path.EndsWith("/@description", StringComparison.Ordinal)), $"The renamed label is expected. {Describe(report)}");
        var leak = Assert.Single(report.Unexpected);
        Expect.Contains("section[name=notes_section]", leak.Path, "The leak into notes_section should be named.");
    }

    // ---- live but missing from the YAML ----

    // Example: tn_fax is deleted from the YAML and the new FormXML still has it -> WouldRemove lists tn_fax; nothing is unexpected.
    [Fact]
    public void Verify_FieldMissingFromTheYaml_IsListedAsWouldRemove()
    {
        var live = SmallLive();

        var report = Verify(live, SmallLive(), ModelOf(live).WithoutControl("tn_fax"));

        var entry = Assert.Single(report.WouldRemove);
        Expect.Contains("tn_fax", entry, "The would-remove entry should name the field.");
        Expect.Empty(report.Unexpected, $"Keeping the field is not an unexpected change. {Describe(report)}");
    }

    [Fact]
    public void Verify_TabMissingFromTheYaml_IsListedAsWouldRemove()
    {
        var live = SmallLive();

        var report = Verify(live, SmallLive(), ModelOf(live).WithoutTab("address"));

        Expect.True(report.WouldRemove.Any(entry => entry.Contains("address", StringComparison.Ordinal)), $"The missing tab should be listed. {Describe(report)}");
    }

    // ---- a move is not a removal ----

    // Example: tn_fax moves from "details" to "notes_section" in the YAML, and the new FormXML has it in the new section only ->
    // WouldRemove is empty, and the changes in the two sections are expected.
    [Fact]
    public void Verify_FieldMovedToAnotherSection_IsNotWouldRemoveAndTheMoveIsExpected()
    {
        var live = SmallLive();
        var fax = ModelOf(live).FindControl("tn_fax");
        var yaml = ModelOf(live)
            .EditSection("general", "details", s => s.EditControls(c => c.Where(x => x.Id != "tn_fax").ToList()))
            .EditSection("general", "notes_section", s => s.EditControls(c => [.. c, fax]));
        var newForm = Changed(f =>
        {
            var cell = CellOf(f, "tn_fax");
            cell.Remove();
            SectionOf(f, "general", "notes_section").Element("rows")!.Add(new XElement("row", cell));
        });

        var report = Verify(live, newForm, yaml);

        Expect.Empty(report.WouldRemove, $"A moved field is not a removal. {Describe(report)}");
        Expect.Empty(report.Unexpected, $"The move touches only the two sections the YAML changed. {Describe(report)}");
        Expect.True(report.Expected.Count > 0, $"The move itself is expected. {Describe(report)}");
    }
}

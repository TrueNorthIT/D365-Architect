using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// A cell with <c>rowspan="N"</c> covers its own row and the N - 1 rows below it, in its own grid column(s). The form
/// designer writes those covered rows out (an empty <c>&lt;row /&gt;</c> in a single-column section, a shorter row in
/// a multi-column one). A control written into a covered position is silently never rendered by the Unified
/// Interface, so every way this tool writes rows must leave the covered positions empty: the from-scratch build
/// (<c>form build-xml</c>, a new form on <c>form import</c>) and the patch of an existing form (<c>form import</c>).
/// </summary>
public sealed class FormRowSpanTests
{
    private static FormControl Text(string id, int? rowSpan = null) =>
        new() { Id = id, Field = id, Label = id, Control = "SingleLineText", RowSpan = rowSpan };

    private static FormDefinition FormWith(int? columns, params FormControl[] controls) => new()
    {
        Name = "Repro",
        Entity = "account",
        Tabs =
        [
            new FormTab
            {
                Name = "tab1",
                Label = "Tab 1",
                Columns =
                [
                    new FormColumn
                    {
                        Width = "100%",
                        Sections = [new FormSection { Name = "sec1", Label = "Section 1", Columns = columns, Controls = controls }],
                    },
                ],
            },
        ],
    };

    private static XElement Build(FormDefinition form) => XElement.Parse(FormXmlWriter.Write(form));

    /// <summary>
    /// Each row of a section as one string: its cells joined by <c>|</c>, a cell being its control id (with
    /// <c>/rsN</c> when it spans N rows) or <c>-</c> for a spacer. An empty row is an empty string.
    /// </summary>
    private static IReadOnlyList<string> RowsOf(XElement section) =>
        section.Element("rows")!.Elements("row").Select(row => string.Join("|", row.Elements("cell").Select(cell =>
        {
            var id = (string?)cell.Element("control")?.Attribute("id") ?? "-";
            return (int?)cell.Attribute("rowspan") is > 1 and var span ? $"{id}/rs{span}" : id;
        }))).ToList();

    /// <summary>Just the row lengths (cells per row), to compare a rebuilt section's shape with the designer's.</summary>
    private static IReadOnlyList<int> ShapeOf(XElement section) =>
        section.Element("rows")!.Elements("row").Select(row => row.Elements("cell").Count()).ToList();

    private static XElement OnlySection(XElement form) => form.Descendants("section").Single();

    // ---- from scratch (form build-xml, a new form) ----

    // Example: the issue's repro: first (rowSpan 3) then second -> first, two empty rows, then second.
    [Fact]
    public void Write_SingleColumnRowSpan_WritesAnEmptyRowForEachCoveredRowBeforeTheNextControl()
    {
        var form = FormWith(null, Text("first", rowSpan: 3), Text("second"));

        var rows = RowsOf(OnlySection(Build(form)));

        Expect.Sequence(["first/rs3", "", "", "second"], rows, "A cell spanning 3 rows covers the 2 rows below it, which must be written empty so the next control starts below the span.");
    }

    // Example: a, b (rowSpan 4), c, d -> a, b, three empty rows, c, d.
    [Fact]
    public void Write_RowSpanFollowedByMoreControls_KeepsEveryLaterControlBelowTheSpan()
    {
        var form = FormWith(null, Text("a"), Text("b", rowSpan: 4), Text("c"), Text("d"));

        var rows = RowsOf(OnlySection(Build(form)));

        Expect.Sequence(["a", "b/rs4", "", "", "", "c", "d"], rows, "Every control after a span should be written below it, in its own row.");
    }

    // Example: a, then b (rowSpan 3) as the last control -> a, b, two empty rows.
    [Fact]
    public void Write_RowSpanAsTheLastControlInTheSection_StillWritesItsCoveredRows()
    {
        var form = FormWith(null, Text("a"), Text("b", rowSpan: 3));

        var rows = RowsOf(OnlySection(Build(form)));

        Expect.Sequence(["a", "b/rs3", "", ""], rows, "The designer writes the covered rows even when nothing follows the span; the build should too.");
    }

    // Example: 2 columns, a | b (rowSpan 3), then c, d, e -> c and d go in the free left column beside the span, e below it.
    [Fact]
    public void Write_TwoColumnsSpanInTheRightColumn_FillsOnlyTheFreeLeftColumnOfTheCoveredRows()
    {
        var form = FormWith(2, Text("a"), Text("b", rowSpan: 3), Text("c"), Text("d"), Text("e"));

        var rows = RowsOf(OnlySection(Build(form)));

        Expect.Sequence(["a|b/rs3", "c", "d", "e"], rows, "A covered row has one free column left of the span, so it takes one control; the span's own column stays empty.");
    }

    // Example: 2 columns, a (rowSpan 3) | b, then c, d -> the covered rows are left empty, c | d below the span.
    [Fact]
    public void Write_TwoColumnsSpanInTheLeftColumn_LeavesTheCoveredRowsEmpty()
    {
        var form = FormWith(2, Text("a", rowSpan: 3), Text("b"), Text("c"), Text("d"));

        var rows = RowsOf(OnlySection(Build(form)));

        Expect.Sequence(["a/rs3|b", "", "", "c|d"], rows, "A control is never written into a row whose first column is covered, since the cell would be read as sitting in that column.");
    }

    // Example: 3 columns, a | b | c (rowSpan 2), then d (colspan 3) -> a full-width control can't sit beside the span, so it goes below it.
    [Fact]
    public void Write_ThreeColumnsWideControlAfterASpan_GoesBelowTheSpan()
    {
        var wide = Text("d");
        var form = FormWith(3, Text("a"), Text("b"), Text("c", rowSpan: 2), wide.With(nameof(FormControl.ColumnSpan), 3));

        var rows = RowsOf(OnlySection(Build(form)));

        Expect.Sequence(["a|b|c/rs2", "", "d"], rows, "A control 3 columns wide doesn't fit beside a span in the third column, so the covered row stays empty.");
    }

    // ---- header and footer ----

    // Example: a header control spanning 2 rows, then another -> an empty row between them.
    [Fact]
    public void Write_HeaderControlWithRowSpan_WritesItsCoveredRows()
    {
        var form = FormWith(null, Text("body")).With(nameof(FormDefinition.HeaderControls), new List<FormControl> { Text("header_a", rowSpan: 2), Text("header_b") });

        var header = Build(form).Element("header")!;
        var rows = header.Element("rows")!.Elements("row").Select(row => row.Elements("cell").Count()).ToList();

        Expect.Sequence([1, 0, 1], rows, "The header writes each control in its own row, so a 2-row span needs one empty row after it.");
    }

    // ---- round trips ----

    // Example: build -> export -> build: same controls, same rowSpans, byte-identical FormXML.
    [Fact]
    public void RoundTrip_BuildThenExportThenBuild_IsStable()
    {
        var form = FormWith(2, Text("a"), Text("b", rowSpan: 3), Text("c"), Text("d"), Text("e", rowSpan: 2));

        var firstXml = FormXmlWriter.Write(form);
        var reread = ThroughYaml(ModelOf(XElement.Parse(firstXml), "Repro", "account"));
        var secondXml = FormXmlWriter.Write(reread);

        var controls = reread.FindSection("sec1").Controls.Select(c => $"{c.Id}/{c.RowSpan ?? 1}").ToList();
        Expect.Sequence(["a/1", "b/3", "c/1", "d/1", "e/2"], controls, "Placeholder rows must not read back as controls, and every rowSpan should come back as written.");
        Expect.Equal(firstXml, secondXml, "Building the exported YAML again should produce the same FormXML.");
    }

    // Example: the Account fixture's designer-written single-column sections with spans (a rowspan 5 field between
    // fields, a rowspan 53 grid followed by a field, ...) -> export, build from scratch -> the same rows, row for row.
    [Theory]
    [InlineData("section_3")]
    [InlineData("section_4")]
    [InlineData("section_6")]
    [InlineData("section_9")]
    [InlineData("section_13")]
    [InlineData("section_23")]
    public void RoundTrip_DesignerSectionWithSpans_RebuildsTheSameRows(string sectionName)
    {
        var live = AccountMain();
        var liveSection = live.Descendants("section").Single(s => (string?)s.Attribute("name") == sectionName);

        var rebuilt = Build(ThroughYaml(AccountModel()));
        var rebuiltSection = rebuilt.Descendants("section").Single(s => (string?)s.Attribute("name") == sectionName);

        Expect.Sequence(ShapeOf(liveSection), ShapeOf(rebuiltSection), $"{sectionName} built from its YAML should have the designer's rows, empty placeholder rows included.");
    }

    // Example: the whole Account fixture: export -> build -> export gives the same controls with the same rowSpans.
    [Fact]
    public void RoundTrip_AccountFixtureExportBuildExport_KeepsEveryControlAndRowSpan()
    {
        var first = ThroughYaml(AccountModel());
        var second = ThroughYaml(ModelOf(Build(first), AccountFormName, "account"));

        static IReadOnlyList<string> Controls(FormDefinition form) =>
            FormXmlWriter.BodyControls(form).Select(c => $"{c.Id}/{c.RowSpan ?? 1}").ToList();

        Expect.Sequence(Controls(first), Controls(second), "Rebuilding the exported form should neither add controls for placeholder rows nor change any rowSpan.");
    }

    // Example: a form this tool created (placeholders included) re-imported unchanged -> the patch and the comparer see no difference.
    [Fact]
    public void RoundTrip_FormBuiltWithSpans_PatchedUnchanged_ReportsNoDifferences()
    {
        var live = Build(FormWith(2, Text("a"), Text("b", rowSpan: 3), Text("c"), Text("d", rowSpan: 2)));

        var differences = FormXmlComparer.Compare(live, Patch(live, ThroughYaml(ModelOf(live, "Repro", "account"))));

        Expect.Empty(differences, $"Placeholder rows written by the build should survive an unchanged patch exactly. {Show(differences)}");
    }

    // ---- patching an existing form (form import) ----

    // Example: a new field (rowSpan 3) added after tn_notes, then another new field -> the second goes below the span.
    [Fact]
    public void Patch_NewControlWithRowSpan_WritesItsCoveredRowsBeforeTheNextNewControl()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditSection("general", "notes_section", s => s.EditControls(c => [.. c, Text("tn_big", rowSpan: 3), Text("tn_after")]));

        var rows = RowsOf(SectionOf(Patch(live, yaml), "general", "notes_section"));

        Expect.Sequence(["tn_notes", "tn_big/rs3", "", "", "tn_after"], rows, "A new spanned control needs its covered rows written, so the field after it isn't hidden under the span.");
    }

    // Example: a new field (rowSpan 2) added above tn_notes -> tn_notes is pushed below the span.
    [Fact]
    public void Patch_NewSpannedControlAboveAnExistingOne_PushesTheExistingOneBelowTheSpan()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditSection("general", "notes_section", s => s.EditControls(c => [Text("tn_big", rowSpan: 2), .. c]));

        var rows = RowsOf(SectionOf(Patch(live, yaml), "general", "notes_section"));

        Expect.Sequence(["tn_big/rs2", "", "tn_notes"], rows, "tn_notes was in the row the new span covers, so an empty row should push it down.");
    }

    // Example: the Account fixture's section_6 has tnfx_field33 (rowspan 5) then four empty rows then tnfx_field34; a new
    // field added straight after tnfx_field33 must go after those rows, not into them.
    [Fact]
    public void Patch_NewControlAfterALiveSpannedCell_GoesBelowTheSpanNotIntoIt()
    {
        var live = AccountMain();
        var yaml = ThroughYaml(AccountModel()).EditSection("tab_1", "section_6", s => s.EditControls(c =>
        {
            var list = c.ToList();
            list.Insert(list.FindIndex(x => x.Id == "tnfx_field33") + 1, Text("tnfx_added"));
            return list;
        }));

        var patched = Patch(live, yaml);

        var rows = RowsOf(SectionOf(patched, "tab_1", "section_6"));
        var spanAt = rows.ToList().IndexOf("tnfx_field33/rs5");
        Expect.Sequence(["tnfx_field33/rs5", "", "", "", "", "tnfx_added", "tnfx_field34"], rows.Skip(spanAt).Take(7).ToList(),
            "The new field should come after the 4 rows the span covers, and the rest of the section should be untouched.");
        var differences = FormXmlComparer.Compare(live, patched);
        Expect.True(differences.All(d => d.Path.Contains("section[name=section_6]", StringComparison.Ordinal)), $"Only section_6 should change. {Show(differences)}");
    }

    // Example: tnfx_field32 (section_6, directly above the spanned tnfx_field33) raised to rowSpan 2 -> an empty row is inserted below it.
    [Fact]
    public void Patch_RowSpanIncreasedOnALiveCell_InsertsTheNewlyCoveredRow()
    {
        var live = AccountMain();
        var yaml = ThroughYaml(AccountModel()).EditControl("tnfx_field32", c => c.With(nameof(FormControl.RowSpan), 2));

        var patched = Patch(live, yaml);

        var rows = RowsOf(SectionOf(patched, "tab_1", "section_6"));
        var at = rows.ToList().IndexOf("tnfx_field32/rs2");
        Expect.True(at >= 0, $"tnfx_field32 should now span 2 rows. Rows: <{string.Join(" , ", rows)}>.");
        Expect.Sequence(["tnfx_field32/rs2", "", "tnfx_field33/rs5", "", "", "", "", "tnfx_field34"], rows.Skip(at).Take(8).ToList(),
            "tnfx_field33 was in the row the larger span now covers, so an empty row should push it (and its own span) down.");

        var report = FormPatchVerifier.Verify(live, patched, AccountModel(), yaml);
        Expect.Empty(report.Unexpected.Select(d => d.Path).ToList(), "Rows pushed down by a changed rowSpan are part of that change, not an unexpected one.");
    }

    // Example: the 2-column "details" section gets a new field spanning 2 rows, then another new field -> the second
    // field goes below the span, since a new row's first cell would otherwise sit in the covered column.
    [Fact]
    public void Patch_MultiColumnSectionNewSpannedControl_KeepsTheNextNewControlOutOfTheCoveredColumn()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditSection("general", "details", s => s.EditControls(c => [.. c, Text("tn_big", rowSpan: 2), Text("tn_after")]));

        var rows = RowsOf(SectionOf(Patch(live, yaml), "general", "details"));

        Expect.Sequence(["tn_name|-", "-|tn_phone", "", "tn_email|tn_fax", "tn_big/rs2", "", "tn_after"], rows,
            "The existing rows stay as they were; the new span's covered row is written empty and the next field goes below it.");
    }

    /// <summary>A live section as the old writer left it: first spans 3 rows but second sits straight under it.</summary>
    private static XElement BrokenLive() => XElement.Parse("""
        <form><tabs><tab name="tab1"><columns><column width="100%"><sections><section name="sec1"><rows>
        <row><cell id="{00000000-0000-4000-8000-000000000001}" rowspan="3"><control id="first" datafieldname="first" /></cell></row>
        <row><cell id="{00000000-0000-4000-8000-000000000002}"><control id="second" datafieldname="second" /></cell></row>
        </rows></section></sections></column></columns></tab></tabs></form>
        """);

    // Example: a section the old writer broke, re-imported unchanged -> left exactly as it is (nothing the YAML asked for).
    [Fact]
    public void Patch_SectionBrokenByAnOlderBuild_UnchangedInTheYaml_IsLeftAlone()
    {
        var live = BrokenLive();

        var differences = FormXmlComparer.Compare(live, Patch(live, ThroughYaml(ModelOf(live, "Repro", "account"))));

        Expect.Empty(differences, $"An unchanged re-import changes nothing, even in a section with a hidden control. {Show(differences)}");
    }

    // Example: the same section with a field added in the YAML -> the patch also writes the missing covered rows.
    [Fact]
    public void Patch_SectionBrokenByAnOlderBuild_EditedInTheYaml_GetsItsCoveredRows()
    {
        var live = BrokenLive();
        var yaml = ModelOf(live, "Repro", "account").EditSection("tab1", "sec1", s => s.EditControls(c => [.. c, Text("third")]));

        var rows = RowsOf(SectionOf(Patch(live, yaml), "tab1", "sec1"));

        Expect.Sequence(["first/rs3", "", "", "second", "third"], rows, "Once the section's layout changes, every control in it should end up clear of the span.");
    }

    // Example: an unchanged re-import of the Account fixture (designer placeholders in 20+ sections) adds no rows.
    [Fact]
    public void Patch_UnchangedAccountFixture_AddsNoRows()
    {
        var live = AccountMain();

        var patched = Patch(live, ThroughYaml(AccountModel()));

        Expect.Equal(live.Descendants("row").Count(), patched.Descendants("row").Count(), "The designer's own covered rows are already right, so nothing should be added.");
    }
}

internal static class FormRowSpanTestExtensions
{
    public static FormSection FindSection(this FormDefinition form, string name) =>
        form.Tabs.SelectMany(t => t.Columns).SelectMany(c => c.Sections).Single(s => s.Name == name);
}

using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Issue #58: on a form the architect did not create, <see cref="FormXmlWriter.Write"/> given the live form
/// (<c>existingForm</c>) must act as a patcher: whatever the YAML did not change stays exactly as it was
/// (spacers, empty rows, section label settings, header layout, <c>locklevel</c>, ids, attributes the model does not
/// know), and only what the YAML changed is written. Each test edits the model the way a person edits the YAML,
/// patches it onto the live FormXML and looks at the result with <see cref="FormXmlComparer"/>.
/// The small form (<see cref="SmallLive"/>) has the awkward features in a few lines; the Account fixture is a real,
/// heavily customised main form (anonymised).
/// </summary>
public sealed class FormPatchWriterTests
{
    private static FormControl NewControl(string id) => new() { Id = id, Field = id, Label = "Extra", Control = "SingleLineText" };

    private static IReadOnlyList<string> RemovalsOf(IReadOnlyList<FormXmlDifference> differences) =>
        differences.Where(d => d.Kind is FormXmlDifferenceKind.ElementRemoved or FormXmlDifferenceKind.AttributeRemoved).Select(d => d.Path).ToList();

    // ---- adding a tab changes only that tab ----

    // Example: add an "Invoice Centres" tab with one subgrid to the YAML -> the only difference is that new tab.
    // Code path: FormXmlWriter.Write(form, existingForm), the tabs block.
    [Fact]
    public void Write_TabAddedToTheYaml_ChangesOnlyThatTab_SmallForm()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).WithTabAppended(InvoiceCentresTab());

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.ElementAdded, difference.Kind, $"Adding a tab should add one element and change nothing else. {Show(differences)}");
        Expect.Contains("tab[name=invoice_centres]", difference.Path, "The added element should be the new tab.");
    }

    // Example: the same on the real Account form: every difference is inside the new tab, none in its 18 existing ones.
    [Fact]
    public void Write_TabAddedToTheAccountFixtureYaml_ChangesOnlyThatTab()
    {
        var live = AccountMain();
        var yaml = ThroughYaml(AccountModel()).WithTabAppended(InvoiceCentresTab());

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        Expect.True(differences.Count > 0, "The new tab should appear in the output.");
        Expect.True(differences.All(d => d.Path.Contains("tab[name=invoice_centres]", StringComparison.Ordinal)), $"Every difference should be inside the new tab. {Show(differences)}");
    }

    // ---- adding a field to an existing section leaves the rest of the section exactly as it was ----

    // Example: add tn_extra to the 2-column "details" section (spacers, an empty row) -> only the new cell appears;
    // every existing cell, spacers included, is at the same row and column.
    [Fact]
    public void Write_FieldAddedToAMultiColumnSection_OnlyTheNewCellAppears_SmallForm()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditSection("general", "details", s => s.EditControls(c => [.. c, NewControl("tn_extra")]));

        var patched = Patch(live, yaml);

        var differences = FormXmlComparer.Compare(live, patched);
        Expect.True(differences.Count > 0 && differences.All(d => d.Kind == FormXmlDifferenceKind.ElementAdded), $"Only additions are expected. {Show(differences)}");
        Expect.True(differences.All(d => d.Path.Contains("section[name=details]", StringComparison.Ordinal)), $"The additions should be inside the details section. {Show(differences)}");
        Expect.Equal(1, CountOfControl(patched, "tn_extra"), "The new field should be on the form once.");
        var after = GridOf(SectionOf(patched, "general", "details"));
        var moved = GridOf(SectionOf(live, "general", "details")).Where(cell => !after.Contains(cell)).ToList();
        Expect.Empty(moved, "Every existing cell (spacers included) should keep its row and column.");
    }

    // Example: the same on the Account fixture's 3-column section_37 (15 spacers, rowspan notes).
    [Fact]
    public void Write_FieldAddedToAMultiColumnSection_OnlyTheNewCellAppears_AccountFixture()
    {
        var live = AccountMain();
        var yaml = ThroughYaml(AccountModel()).EditSection("tab_17", "section_37", s => s.EditControls(c => [.. c, NewControl("tnfx_added")]));

        var patched = Patch(live, yaml);

        var differences = FormXmlComparer.Compare(live, patched);
        Expect.True(differences.Count > 0 && differences.All(d => d.Kind == FormXmlDifferenceKind.ElementAdded), $"Only additions are expected. {Show(differences)}");
        Expect.True(differences.All(d => d.Path.Contains("section[name=section_37]", StringComparison.Ordinal)), $"The additions should be inside section_37. {Show(differences)}");
        var after = GridOf(SectionOf(patched, "tab_17", "section_37"));
        var moved = GridOf(SectionOf(live, "tab_17", "section_37")).Where(cell => !after.Contains(cell)).ToList();
        Expect.Empty(moved, "Every existing cell (spacers and the rowspan notes field included) should keep its row and column.");
    }

    // ---- changing a property changes only that property ----

    // Example: rename the label of tn_name (a locklevel="1" cell) -> one difference, the label text; the cell keeps its id and locklevel.
    [Fact]
    public void Write_FieldLabelRenamed_ChangesOnlyTheLabelText_SmallForm()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_name", c => c.With(nameof(FormControl.Label), "Full name"));

        var patched = Patch(live, yaml);

        var differences = FormXmlComparer.Compare(live, patched);
        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.AttributeChanged, difference.Kind, $"A renamed label is one changed attribute. {Show(differences)}");
        Expect.True(difference.Path.EndsWith("/@description", StringComparison.Ordinal), $"The label text is the label's description attribute. Path: <{difference.Path}>.");
        Expect.Equal("Name", difference.Before, "Before is the old label.");
        Expect.Equal("Full name", difference.After, "After is the new label.");
        Expect.Equal((string?)CellOf(live, "tn_name").Attribute("id"), (string?)CellOf(patched, "tn_name").Attribute("id"), "The cell should keep its id.");
        Expect.Equal("1", (string?)CellOf(patched, "tn_name").Attribute("locklevel"), "The cell should keep locklevel=1.");
        Expect.Equal("200", (string?)SectionOf(patched, "general", "details").Attribute("labelwidth"), "The section should keep its labelwidth.");
    }

    // Example: the same rename on the Account fixture's first cell (locklevel="1") -> still a single difference.
    [Fact]
    public void Write_FieldLabelRenamed_ChangesOnlyTheLabelText_AccountFixture()
    {
        var live = AccountMain();
        var yaml = ThroughYaml(AccountModel()).EditControl("tnfx_field1", c => c.With(nameof(FormControl.Label), "Renamed"));

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        var difference = Assert.Single(differences);
        Expect.True(difference.Path.EndsWith("/@description", StringComparison.Ordinal), $"Only the label text should differ. {Show(differences)}");
        Expect.Equal("Renamed", difference.After, "After is the new label.");
    }

    // Example: set disabled on tn_phone -> only control[id=tn_phone]/@disabled changes; its uniqueid and the rest stay.
    [Fact]
    public void Write_ControlPropertyChanged_ChangesOnlyThatAttribute()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_phone", c => c.With(nameof(FormControl.Disabled), true));

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.AttributeChanged, difference.Kind, $"disabled false -> true is one changed attribute. {Show(differences)}");
        Expect.Contains("control[id=tn_phone]", difference.Path, "The control is addressed by its id.");
        Expect.True(difference.Path.EndsWith("/@disabled", StringComparison.Ordinal), $"Only disabled should change. Path: <{difference.Path}>.");
    }

    // ---- existing elements keep their ids; only new elements get generated ones ----

    // Example: tab "general" keeps {A1B2C3D4-0001-...} (upper case) and "address" keeps its lower-case id; sections keep theirs.
    [Fact]
    public void Write_ExistingTabsAndSections_KeepTheirLiveIds()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_name", c => c.With(nameof(FormControl.Label), "Full name"));

        var patched = Patch(live, yaml);

        foreach (var (tab, section) in new[] { ("general", "details"), ("general", "notes_section"), ("address", "address_section") })
        {
            Expect.Equal((string?)live.Element("tabs")!.Elements("tab").Single(t => (string?)t.Attribute("name") == tab).Attribute("id"),
                (string?)patched.Element("tabs")!.Elements("tab").Single(t => (string?)t.Attribute("name") == tab).Attribute("id"), $"Tab '{tab}' should keep its live id.");
            Expect.Equal((string?)SectionOf(live, tab, section).Attribute("id"), (string?)SectionOf(patched, tab, section).Attribute("id"), $"Section '{section}' should keep its live id.");
        }
    }

    // Example: cells are matched by their control id (tn_phone keeps {A1B2C3D4-0014-...}); header and footer cells and the header's own id too.
    [Fact]
    public void Write_ExistingCellsHeaderAndFooter_KeepTheirLiveIds()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_name", c => c.With(nameof(FormControl.Label), "Full name"));

        var patched = Patch(live, yaml);

        foreach (var control in new[] { "tn_name", "tn_phone", "tn_email", "tn_fax", "tn_notes", "tn_street", "header_tn_owner", "header_tn_status", "footer_tn_modified" })
        {
            Expect.Equal((string?)CellOf(live, control).Attribute("id"), (string?)CellOf(patched, control).Attribute("id"), $"The cell of '{control}' should keep its live id.");
        }

        Expect.Equal((string?)live.Element("header")!.Attribute("id"), (string?)patched.Element("header")!.Attribute("id"), "The header should keep its live id.");
        Expect.Equal((string?)live.Element("footer")!.Attribute("id"), (string?)patched.Element("footer")!.Attribute("id"), "The footer should keep its live id.");
    }

    // Example: handler Tn.onLoad (matched by functionName + libraryName) keeps handlerUniqueId d1d1d1d1-0002-...; library tn_/lib.js keeps libraryUniqueId.
    [Fact]
    public void Write_ExistingHandlersAndLibraries_KeepTheirLiveIds()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_name", c => c.With(nameof(FormControl.Label), "Full name"));

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        Expect.True(!differences.Any(d => d.Path.Contains("Handler[", StringComparison.Ordinal) || d.Path.Contains("Library[", StringComparison.Ordinal)),
            $"No handler or library should differ (ids included) when the YAML did not touch them. {Show(differences)}");
    }

    // Example: edit one label on the Account fixture -> no tab, section, cell, handler or library id changes anywhere.
    [Fact]
    public void Write_AnyEditOnTheAccountFixture_ChangesNoExistingId()
    {
        var live = AccountMain();
        var yaml = ThroughYaml(AccountModel()).EditControl("tnfx_field1", c => c.With(nameof(FormControl.Label), "Renamed"));

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        var idChanges = differences.Where(d => d.Path.EndsWith("/@id", StringComparison.Ordinal) || d.Path.EndsWith("/@labelid", StringComparison.Ordinal)
            || d.Path.EndsWith("/@uniqueid", StringComparison.Ordinal) || d.Path.EndsWith("UniqueId", StringComparison.Ordinal)).ToList();
        Expect.Empty(idChanges, $"Existing elements must keep every id. {Show(idChanges)}");
    }

    // Example: the new "invoice_centres" tab gets a GUID, the same one on every build, and not one already used on the form.
    [Fact]
    public void Write_NewTab_GetsAFreshIdThatIsTheSameOnEveryBuild()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).WithTabAppended(InvoiceCentresTab());

        string NewTabId() => (string?)Patch(live, yaml).Element("tabs")!.Elements("tab").Single(t => (string?)t.Attribute("name") == "invoice_centres").Attribute("id") ?? "";
        var first = NewTabId();

        Expect.True(Guid.TryParse(first, out _), $"The new tab's id should be a GUID. Actual: <{first}>.");
        Expect.Equal(first, NewTabId(), "Building twice should give the new tab the same id (deterministic).");
        var existingIds = live.Descendants().Select(e => (string?)e.Attribute("id")).Where(id => id is not null).ToList();
        Expect.True(!existingIds.Contains(first, StringComparer.OrdinalIgnoreCase), "The new tab's id should not collide with an id already on the form.");
    }

    // ---- header and footer are left alone unless the YAML changes their fields ----

    // Example: edit a body tab only -> the header and footer elements (columns="111", celllabelposition, row layout) are identical.
    [Fact]
    public void Write_BodyTabEditedOnly_LeavesHeaderAndFooterExactlyAsTheyWere_SmallForm()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).EditControl("tn_street", c => c.With(nameof(FormControl.Label), "Street name"));

        var patched = Patch(live, yaml);

        Expect.True(XNode.DeepEquals(live.Element("header"), patched.Element("header")), $"The header should be unchanged. Actual: <{patched.Element("header")}>.");
        Expect.True(XNode.DeepEquals(live.Element("footer"), patched.Element("footer")), $"The footer should be unchanged. Actual: <{patched.Element("footer")}>.");
    }

    // Example: the Account form's header (columns="111", celllabelposition="Top") survives a body edit.
    [Fact]
    public void Write_BodyTabEditedOnly_LeavesTheAccountHeaderLayoutAlone()
    {
        var live = AccountMain();
        var yaml = ThroughYaml(AccountModel()).EditControl("tnfx_field1", c => c.With(nameof(FormControl.Label), "Renamed"));

        var patched = Patch(live, yaml);

        Expect.Equal("111", (string?)patched.Element("header")?.Attribute("columns"), "The header's columns attribute should be unchanged.");
        Expect.True(XNode.DeepEquals(live.Element("header"), patched.Element("header")), "The header should be unchanged.");
        Expect.True(XNode.DeepEquals(live.Element("footer"), patched.Element("footer")), "The footer should be unchanged.");
    }

    // Example: rename the label of header_tn_owner in the YAML -> only that label differs; the header keeps columns="111" and its row.
    [Fact]
    public void Write_HeaderControlChanged_ChangesOnlyThatControlAndKeepsTheHeaderLayout()
    {
        var live = SmallLive();
        var model = ModelOf(live);
        var yaml = model.With(nameof(FormDefinition.HeaderControls),
            model.HeaderControls!.Select(c => c.Id == "header_tn_owner" ? c.With(nameof(FormControl.Label), "Account owner") : c).ToList());

        var patched = Patch(live, yaml);

        var differences = FormXmlComparer.Compare(live, patched);
        var difference = Assert.Single(differences);
        Expect.True(difference.Path.StartsWith("header", StringComparison.Ordinal) && difference.Path.EndsWith("/@description", StringComparison.Ordinal), $"Only the header label should differ. {Show(differences)}");
        Expect.Equal("111", (string?)patched.Element("header")!.Attribute("columns"), "The header should keep its columns attribute.");
        Expect.Equal(2, patched.Element("header")!.Elements("rows").Elements("row").Elements("cell").Count(), "The header should keep its row and cells.");
    }

    // ---- anything live but missing from the YAML is kept; removed only with allowRemovals ----

    // Example: delete tn_fax from the YAML -> the patched form is identical to the live one.
    [Fact]
    public void Write_FieldDeletedFromTheYaml_StaysOnTheFormByDefault()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).WithoutControl("tn_fax");

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        Expect.Empty(differences, $"A field missing from the YAML must stay on the form unless removals are allowed. {Show(differences)}");
    }

    // Example: the same delete with allowRemovals -> tn_fax is gone, every other cell (spacers too) is where it was.
    [Fact]
    public void Write_FieldDeletedFromTheYamlWithAllowRemovals_RemovesOnlyThatField()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).WithoutControl("tn_fax");

        var patched = Patch(live, yaml, allowRemovals: true);

        Expect.Equal(0, CountOfControl(patched, "tn_fax"), "With removals allowed, the deleted field should be removed.");
        var removals = RemovalsOf(FormXmlComparer.Compare(live, patched));
        Expect.True(removals.Count > 0 && removals.All(p => p.Contains("section[name=details]", StringComparison.Ordinal)), $"Only the details section should lose anything. Removals: <{string.Join(", ", removals)}>.");
        var after = GridOf(SectionOf(patched, "general", "details"));
        var moved = GridOf(SectionOf(live, "general", "details")).Where(c => !c.EndsWith(":tn_fax", StringComparison.Ordinal) && !after.Contains(c)).ToList();
        Expect.Empty(moved, "Every other cell should keep its row and column.");
    }

    // Example: delete the "address" tab from the YAML -> the tab stays, complete.
    [Fact]
    public void Write_TabDeletedFromTheYaml_StaysOnTheFormByDefault()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).WithoutTab("address");

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        Expect.Empty(differences, $"A tab missing from the YAML must stay on the form unless removals are allowed. {Show(differences)}");
    }

    [Fact]
    public void Write_TabDeletedFromTheYamlWithAllowRemovals_RemovesTheTab()
    {
        var live = SmallLive();
        var yaml = ModelOf(live).WithoutTab("address");

        var patched = Patch(live, yaml, allowRemovals: true);

        Expect.True(!patched.Element("tabs")!.Elements("tab").Any(t => (string?)t.Attribute("name") == "address"), "With removals allowed, the deleted tab should be removed.");
        Expect.Equal(1, patched.Element("tabs")!.Elements("tab").Count(), "Only the deleted tab should go.");
    }

    // Example: the YAML drops the custom Tn.onLoad handler -> it stays on the onload event, with its id and the system handler beside it.
    [Fact]
    public void Write_HandlerDeletedFromTheYaml_StaysOnTheFormByDefault()
    {
        var live = SmallLive();
        var model = ModelOf(live);
        var yaml = model.With(nameof(FormDefinition.Events), model.Events!.Select(e => e.With(nameof(FormEvent.Handlers), null)).ToList());

        var differences = FormXmlComparer.Compare(live, Patch(live, yaml));

        Expect.Empty(differences, $"A handler missing from the YAML must stay on the form unless removals are allowed. {Show(differences)}");
    }

    // ---- moving a field to another section is a move, not a removal ----

    // Example: tn_fax moves from "details" to "notes_section" in the YAML -> it ends up there, once, with no allowRemovals.
    [Fact]
    public void Write_FieldMovedToAnotherSection_EndsUpInTheNewSectionWithoutAllowRemovals()
    {
        var live = SmallLive();
        var yaml = ModelOf(live)
            .EditSection("general", "details", s => s.EditControls(c => c.Where(x => x.Id != "tn_fax").ToList()))
            .EditSection("general", "notes_section", s => s.EditControls(c => [.. c, ModelOf(live).FindControl("tn_fax")]));

        var patched = Patch(live, yaml);

        Expect.Equal(1, CountOfControl(patched, "tn_fax"), "A moved field should be on the form exactly once.");
        Expect.True(SectionOf(patched, "general", "notes_section").Descendants("control").Any(c => (string?)c.Attribute("id") == "tn_fax"), "tn_fax should be in notes_section.");
        Expect.True(!SectionOf(patched, "general", "details").Descendants("control").Any(c => (string?)c.Attribute("id") == "tn_fax"), "tn_fax should no longer be in details.");
    }

    // Example: tn_street moves from the "address" tab to "notes_section" on "general".
    [Fact]
    public void Write_FieldMovedToAnotherTab_EndsUpInTheNewSectionWithoutAllowRemovals()
    {
        var live = SmallLive();
        var street = ModelOf(live).FindControl("tn_street");
        var yaml = ModelOf(live)
            .EditSection("address", "address_section", s => s.EditControls(c => c.Where(x => x.Id != "tn_street").ToList()))
            .EditSection("general", "notes_section", s => s.EditControls(c => [.. c, street]));

        var patched = Patch(live, yaml);

        Expect.Equal(1, CountOfControl(patched, "tn_street"), "A moved field should be on the form exactly once.");
        Expect.True(SectionOf(patched, "general", "notes_section").Descendants("control").Any(c => (string?)c.Attribute("id") == "tn_street"), "tn_street should be in notes_section on the general tab.");
    }
}

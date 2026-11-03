using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// The FormXML comparer (issues #58/#59): reports every difference between two <c>&lt;form&gt;</c> elements
/// except whitespace, attribute order and the XML declaration, so a test (or CI) can prove a rebuild lost nothing.
/// Code path: <see cref="FormXmlComparer.Compare"/>. Paths are the <c>/</c>-separated element names from below
/// <c>&lt;form&gt;</c> (<c>tab[name=X]</c>, <c>section[name=X]</c>, <c>control[id=X]</c>, <c>Library[name=X]</c>,
/// <c>Handler[functionName=X]</c>, otherwise <c>name[position]</c>), with attribute differences ending in <c>/@attr</c>.
/// </summary>
public sealed class FormXmlComparerTests
{
    private const string Grid = """
        <tabs><tab name="tab_1" showlabel="true"><columns><column><sections>
          <section name="s1"><rows>
            <row><cell id="{AAAA}"><control id="tn_a" disabled="false" /></cell><cell id="{BBBB}" /></row>
            <row />
          </rows></section>
        </sections></column></columns></tab></tabs>
        """;

    private static XElement Form(string inner, string attributes = "") => XElement.Parse($"<form{attributes}>{inner}</form>");

    private static IReadOnlyList<FormXmlDifference> Compare(string before, string after) => FormXmlComparer.Compare(Form(before), Form(after));

    // ---- what is ignored ----

    // Example: the same form pretty-printed and compacted -> no differences. Code path: FormXmlComparer.Compare ignoring whitespace-only text.
    [Fact]
    public void Compare_SameFormWithDifferentWhitespace_ReportsNothing()
    {
        var pretty = XElement.Parse(Form(Grid).ToString(SaveOptions.None));
        var compact = XElement.Parse(Form(Grid).ToString(SaveOptions.DisableFormatting));

        var differences = FormXmlComparer.Compare(pretty, compact);

        Expect.Empty(differences, "Whitespace-only text between elements is not a difference.");
    }

    // Example: <tab name="a" id="1"> vs <tab id="1" name="a"> -> no differences. Code path: FormXmlComparer.Compare comparing attributes by name.
    [Fact]
    public void Compare_AttributesInADifferentOrder_ReportsNothing()
    {
        var differences = Compare("""<tabs><tab name="a" id="{1}" locklevel="0" /></tabs>""", """<tabs><tab locklevel="0" id="{1}" name="a" /></tabs>""");

        Expect.Empty(differences, "Attribute order is not a difference.");
    }

    // Example: one side parsed from a document with <?xml version="1.0" encoding="utf-8"?>, the other without -> no differences.
    [Fact]
    public void Compare_XmlDeclarationOnOnlyOneSide_ReportsNothing()
    {
        var withDeclaration = XDocument.Parse("""<?xml version="1.0" encoding="utf-8"?><form><tabs /></form>""").Root!;
        var withoutDeclaration = XDocument.Parse("<form><tabs /></form>").Root!;

        var differences = FormXmlComparer.Compare(withDeclaration, withoutDeclaration);

        Expect.Empty(differences, "The XML declaration is not a difference.");
    }

    // ---- what is reported ----

    // Example: showlabel="true" dropped from a tab -> AttributeRemoved at .../tab[name=tab_1]/@showlabel, before "true".
    [Fact]
    public void Compare_AttributeDropped_ReportsAttributeRemovedWithItsPath()
    {
        var differences = Compare("""<tabs><tab name="tab_1" showlabel="true" /></tabs>""", """<tabs><tab name="tab_1" /></tabs>""");

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.AttributeRemoved, difference.Kind, $"A dropped attribute is AttributeRemoved. {Show(differences)}");
        Expect.Contains("tab[name=tab_1]", difference.Path, "The tab is addressed by its name.");
        Expect.True(difference.Path.EndsWith("/@showlabel", StringComparison.Ordinal), $"An attribute difference ends in /@attribute. Path: <{difference.Path}>.");
        Expect.Equal("true", difference.Before, "Before is the dropped attribute's value.");
    }

    [Fact]
    public void Compare_AttributeAdded_ReportsAttributeAddedWithItsValue()
    {
        var differences = Compare("""<tabs><tab name="tab_1" /></tabs>""", """<tabs><tab name="tab_1" locklevel="1" /></tabs>""");

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.AttributeAdded, difference.Kind, $"A new attribute is AttributeAdded. {Show(differences)}");
        Expect.True(difference.Path.EndsWith("/@locklevel", StringComparison.Ordinal), $"Path should end in /@locklevel. Path: <{difference.Path}>.");
        Expect.Equal("1", difference.After, "After is the new attribute's value.");
    }

    // Example: tab id {9277B590-...} becomes {9277b590-...} (only the case differs) -> AttributeChanged with both values.
    [Fact]
    public void Compare_GuidCaseChanged_ReportsAttributeChangedWithBothValues()
    {
        var differences = Compare("""<tabs><tab name="tab_1" id="{9277B590-413F-254A-A1F5-B3AA67511939}" /></tabs>""", """<tabs><tab name="tab_1" id="{9277b590-413f-254a-a1f5-b3aa67511939}" /></tabs>""");

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.AttributeChanged, difference.Kind, $"Values are compared case-sensitively, so a GUID in another case is a change. {Show(differences)}");
        Expect.Equal("{9277B590-413F-254A-A1F5-B3AA67511939}", difference.Before, "Before keeps its original case.");
        Expect.Equal("{9277b590-413f-254a-a1f5-b3aa67511939}", difference.After, "After keeps its original case.");
    }

    [Fact]
    public void Compare_LabelTextInADifferentCase_ReportsAttributeChanged()
    {
        var differences = Compare("""<tabs><tab name="t"><labels><label description="General" /></labels></tab></tabs>""", """<tabs><tab name="t"><labels><label description="GENERAL" /></labels></tab></tabs>""");

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.AttributeChanged, difference.Kind, $"Comparison is case-sensitive. {Show(differences)}");
    }

    // Example: a spacer cell (no control) removed from the end of a row -> ElementRemoved at .../section[name=s1]/.../row[1]/cell[2].
    [Fact]
    public void Compare_EmptySpacerCellRemoved_ReportsElementRemoved()
    {
        var before = Form(Grid);
        var after = Form(Grid);
        after.Descendants("cell").Single(c => (string?)c.Attribute("id") == "{BBBB}").Remove();

        var differences = FormXmlComparer.Compare(before, after);

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.ElementRemoved, difference.Kind, $"A removed spacer cell is an ElementRemoved. {Show(differences)}");
        Expect.Contains("section[name=s1]", difference.Path, "The cell sits in section s1.");
        Expect.True(difference.Path.EndsWith("/cell[2]", StringComparison.Ordinal), $"Unkeyed elements are addressed by 1-based position; the spacer is the second cell. Path: <{difference.Path}>.");
    }

    [Fact]
    public void Compare_EmptyRowRemoved_ReportsElementRemoved()
    {
        var before = Form(Grid);
        var after = Form(Grid);
        after.Descendants("row").Last().Remove();

        var differences = FormXmlComparer.Compare(before, after);

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.ElementRemoved, difference.Kind, $"A removed empty row is an ElementRemoved. {Show(differences)}");
        Expect.True(difference.Path.EndsWith("/row[2]", StringComparison.Ordinal), $"The empty row is the second row. Path: <{difference.Path}>.");
    }

    [Fact]
    public void Compare_NewTab_ReportsElementAddedAtTheTab()
    {
        var differences = Compare("""<tabs><tab name="tab_1" /></tabs>""", """<tabs><tab name="tab_1" /><tab name="tab_2" /></tabs>""");

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.ElementAdded, difference.Kind, $"A new tab is an ElementAdded. {Show(differences)}");
        Expect.True(difference.Path.EndsWith("tab[name=tab_2]", StringComparison.Ordinal), $"The new tab is addressed by its name. Path: <{difference.Path}>.");
    }

    [Fact]
    public void Compare_ElementTextChanged_ReportsTextChangedWithBothValues()
    {
        var differences = Compare(
            """<tabs><tab name="t"><control id="tn_a"><parameters><MaxLength>100</MaxLength></parameters></control></tab></tabs>""",
            """<tabs><tab name="t"><control id="tn_a"><parameters><MaxLength>200</MaxLength></parameters></control></tab></tabs>""");

        var difference = Assert.Single(differences);
        Expect.Equal(FormXmlDifferenceKind.TextChanged, difference.Kind, $"Changed element text is TextChanged. {Show(differences)}");
        Expect.Equal("100", difference.Before, "Before is the old text.");
        Expect.Equal("200", difference.After, "After is the new text.");
        Expect.Contains("control[id=tn_a]", difference.Path, "A control is addressed by its id.");
    }

    [Fact]
    public void Compare_LibraryAttributeDropped_AddressesTheLibraryByName()
    {
        var differences = Compare(
            """<formLibraries><Library name="a.js" libraryUniqueId="{1}" /></formLibraries>""",
            """<formLibraries><Library name="a.js" /></formLibraries>""");

        var difference = Assert.Single(differences);
        Expect.Contains("Library[name=a.js]", difference.Path, "A library is addressed by its name.");
    }

    [Fact]
    public void Compare_HandlerAttributeChanged_AddressesTheHandlerByFunctionName()
    {
        var differences = Compare(
            """<events><event name="onload"><Handlers><Handler functionName="Tn.onLoad" enabled="true" /></Handlers></event></events>""",
            """<events><event name="onload"><Handlers><Handler functionName="Tn.onLoad" enabled="false" /></Handlers></event></events>""");

        var difference = Assert.Single(differences);
        Expect.Contains("Handler[functionName=Tn.onLoad]", difference.Path, "A handler is addressed by its function name.");
        Expect.True(difference.Path.EndsWith("/@enabled", StringComparison.Ordinal), $"The changed attribute is enabled. Path: <{difference.Path}>.");
    }

    // Example: three unrelated edits -> three differences, one of each kind, nothing swallowed.
    [Fact]
    public void Compare_SeveralDifferences_ReportsEveryOne()
    {
        var before = Form(Grid);
        var after = Form(Grid);
        after.Descendants("tab").Single().SetAttributeValue("showlabel", null);
        after.Descendants("control").Single().SetAttributeValue("disabled", "true");
        after.Descendants("row").Last().Remove();

        var differences = FormXmlComparer.Compare(before, after);

        Expect.Equal(3, differences.Count, $"Every difference should be reported. {Show(differences)}");
        Expect.True(differences.Any(d => d.Kind == FormXmlDifferenceKind.AttributeRemoved && d.Path.EndsWith("/@showlabel", StringComparison.Ordinal)), $"The dropped showlabel should be reported. {Show(differences)}");
        Expect.True(differences.Any(d => d.Kind == FormXmlDifferenceKind.AttributeChanged && d.Path.EndsWith("/@disabled", StringComparison.Ordinal)), $"The changed disabled should be reported. {Show(differences)}");
        Expect.True(differences.Any(d => d.Kind == FormXmlDifferenceKind.ElementRemoved), $"The removed row should be reported. {Show(differences)}");
    }
}

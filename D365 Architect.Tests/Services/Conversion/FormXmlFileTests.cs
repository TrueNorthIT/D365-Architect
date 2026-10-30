using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.FormPatchFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Reading and rewriting a local FormXML file (issue #59, the offline round trip): either a solution export
/// (<c>&lt;forms&gt;&lt;systemform&gt;...&lt;form&gt;</c>, with the form id and names around the form) or a bare
/// <c>&lt;form&gt;</c>. Code path: <see cref="FormXmlFile.LoadForm"/> and <see cref="FormXmlFile.ReplaceForm"/>.
/// The Account fixture is a solution export with a byte order mark.
/// </summary>
public sealed class FormXmlFileTests
{
    // Example: Fixtures/FormXml/account-main.xml (solution export, with a BOM) -> its <form>, tabs and all.
    [Fact]
    public void LoadForm_SolutionExportFile_ReturnsTheFormElement()
    {
        var form = FormXmlFile.LoadForm(FixturePath("account-main.xml"));

        Expect.Equal("form", form.Name.LocalName, "LoadForm should return the <form> element, not the <forms> wrapper.");
        Expect.True(form.Element("tabs")?.Elements("tab").Any() == true, "The returned form should carry its tabs.");
        Expect.Equal("HighWithControls", (string?)form.Attribute("headerdensity"), "The form's own attributes should be intact.");
    }

    [Fact]
    public void LoadForm_BareFormFileWithByteOrderMark_ReturnsTheFormElement()
    {
        using var directory = new TempDirectory();
        var path = directory.File("bare.xml");
        File.WriteAllText(path, """<form showImage="true"><tabs><tab name="t1" /></tabs></form>""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var form = FormXmlFile.LoadForm(path);

        Expect.Equal("form", form.Name.LocalName, "A bare <form> file should load as that form.");
        Expect.Equal("t1", (string?)form.Element("tabs")?.Element("tab")?.Attribute("name"), "The bare form's content should be intact.");
    }

    // Example: a new <form> replaces the fixture's; <formid>, <LocalizedNames> and everything outside <form> stays byte-for-byte.
    [Fact]
    public void ReplaceForm_SolutionExportText_ReplacesOnlyTheFormElement()
    {
        var original = File.ReadAllText(FixturePath("account-main.xml"));
        var newForm = AccountMain();
        newForm.SetAttributeValue("maxWidth", "1800");

        var result = FormXmlFile.ReplaceForm(original, newForm);

        var start = Regex.Match(original, @"<form[\s>]").Index;
        var end = original.LastIndexOf("</form>", StringComparison.Ordinal) + "</form>".Length;
        var resultStart = Regex.Match(result, @"<form[\s>]").Index;
        var resultEnd = result.LastIndexOf("</form>", StringComparison.Ordinal) + "</form>".Length;
        Expect.Equal(original[..start], result[..resultStart], "Everything before <form> (declaration, <forms>, <formid>, ...) should be preserved.");
        Expect.Equal(original[end..], result[resultEnd..], "Everything after </form> (<LocalizedNames>, <Descriptions>, ...) should be preserved.");
        Expect.Equal("1800", (string?)XElement.Parse(result[resultStart..resultEnd]).Attribute("maxWidth"), "The <form> element should be the new one.");
    }

    [Fact]
    public void ReplaceForm_BareFormText_ReturnsTheNewForm()
    {
        var newForm = XElement.Parse("""<form showImage="false"><tabs /></form>""");

        var result = FormXmlFile.ReplaceForm("""<form showImage="true"><tabs><tab name="t1" /></tabs></form>""", newForm);

        Expect.True(XNode.DeepEquals(newForm, XElement.Parse(result)), $"A bare file should become the new form. Actual: <{result}>.");
    }
}

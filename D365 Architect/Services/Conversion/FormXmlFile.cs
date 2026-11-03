using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reads and rewrites a local FormXML file, so the form round trip can run
/// with no sign-in (issue #59): either a solution export's
/// <c>Entities/&lt;table&gt;/FormXml/&lt;type&gt;/{id}.xml</c>
/// (<c>forms/systemform/form</c>, with the form id and names around it) or a
/// bare <c>&lt;form&gt;</c>, the shape the Web API's <c>formxml</c> holds.
/// </summary>
public static partial class FormXmlFile
{
    /// <summary>
    /// The <c>&lt;form&gt;</c> element of the file at <paramref name="path"/>.
    /// Insignificant whitespace is dropped, so it reads like the compact
    /// <c>formxml</c> the Web API returns.
    /// </summary>
    public static XElement LoadForm(string path) => LoadLocalForm(path).Form;

    /// <summary>
    /// The form plus whatever identity a solution export carries around it:
    /// <c>&lt;formid&gt;</c> and the 1033 (else first) <c>&lt;LocalizedName&gt;</c>.
    /// Both are null for a bare <c>&lt;form&gt;</c> file, which has neither.
    /// </summary>
    public static LocalForm LoadLocalForm(string path)
    {
        var root = XDocument.Load(path).Root
            ?? throw new InvalidDataException($"'{path}' has no root element.");

        if (root.Name.LocalName == "form")
        {
            return new LocalForm(root, FormId: null, Name: null);
        }

        var systemForm = root.Element("systemform");
        var form = systemForm?.Element("form")
            ?? throw new InvalidDataException($"'{path}' is neither a bare <form> nor a solution export (<forms><systemform><form>).");

        var formId = Guid.TryParse(systemForm!.Element("formid")?.Value, out var id) ? id : (Guid?)null;
        var names = systemForm.Element("LocalizedNames")?.Elements("LocalizedName").ToList() ?? [];
        var name = (names.FirstOrDefault(n => (string?)n.Attribute("languagecode") == "1033") ?? names.FirstOrDefault())?.Attribute("description")?.Value;

        return new LocalForm(form, formId, name);
    }

    /// <summary>
    /// <paramref name="originalFileText"/> with only its <c>&lt;form&gt;</c>
    /// element replaced by <paramref name="newForm"/>. Done on the text rather
    /// than by re-serializing the document, so everything around the form —
    /// declaration, <c>&lt;formid&gt;</c>, <c>&lt;LocalizedNames&gt;</c>, their
    /// formatting — stays byte-for-byte.
    /// </summary>
    public static string ReplaceForm(string originalFileText, XElement newForm)
    {
        var start = FormStartTag().Match(originalFileText);
        var end = originalFileText.LastIndexOf("</form>", StringComparison.Ordinal);
        if (!start.Success || end < start.Index)
        {
            throw new InvalidDataException("The file has no <form> element to replace.");
        }

        end += "</form>".Length;
        return string.Concat(originalFileText.AsSpan(0, start.Index), newForm.ToString(SaveOptions.DisableFormatting), originalFileText.AsSpan(end));
    }

    // `<form ` or `<form>`, never `<forms>`/`<formid>`.
    [GeneratedRegex(@"<form[\s>]")]
    private static partial Regex FormStartTag();
}

/// <param name="Form">The <c>&lt;form&gt;</c> element.</param>
/// <param name="FormId">The solution export's <c>&lt;formid&gt;</c>; null for a bare form.</param>
/// <param name="Name">The solution export's form name; null for a bare form.</param>
public sealed record LocalForm(XElement Form, Guid? FormId, string? Name);

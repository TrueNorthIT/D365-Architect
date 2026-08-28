using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Validates FormXML (as produced by <see cref="FormXmlWriter"/>) against
/// Microsoft's own official FormXML XSD schema — see
/// `Resources/FormXmlSchema/NOTICE.md` for exactly which files, their
/// provenance, and a confirmed case where even this authoritative schema
/// disagrees with real, live Dataverse output (a form's own
/// <c>headerdensity</c>/<c>showinformselector</c> attributes, present on
/// every real form checked, aren't declared anywhere in the schema and
/// there's no wildcard attribute to fall back on) — a violation here isn't
/// automatically a bug in this tool's own output.
///
/// That does NOT generalise to every violation, though — see
/// <see cref="FormXmlValidationMessage.IsKnownHarmless"/>'s own doc comment
/// for a confirmed case where the opposite happened: a different violation
/// (an invalid child element inside a control's <c>&lt;parameters&gt;</c>)
/// was once assumed similarly harmless on this same reasoning and turned
/// out to make Dataverse's own write-time validation reject the request
/// outright with a 400. So only the one specifically-confirmed pattern is
/// safe to treat as a non-blocking warning; every other violation should be
/// treated as a real reason to refuse writing the file until a human has
/// actually looked at it — <c>form import</c> does exactly that (see
/// <c>ImportFormCommand</c>'s own <c>--allow-schema-violations</c> escape
/// hatch for once they have). Each result is a
/// <see cref="FormXmlValidationMessage"/> — the validator's own severity,
/// position, message, and a ready-to-print snippet of the offending FormXML
/// (see that type's own doc comment for why a snippet matters here
/// specifically).
/// </summary>
public static class FormXmlValidator
{
    // Confirmed empirically (Assembly.GetManifestResourceNames()): embedded
    // resource names are derived from the project's RootNamespace + folder
    // path, not its AssemblyName ("d365architect") — the two happen to
    // differ only in casing here, but that's specifically why this is a
    // fixed literal rather than derived from the running assembly's own
    // name.
    private const string ResourcePrefix = "D365Architect.Resources.FormXmlSchema.";

    private static readonly Lazy<XmlSchemaSet> SchemaSet = new(LoadSchemaSet);

    /// <returns>Every schema violation found, in document order; empty when the document is fully valid.</returns>
    /// <exception cref="XmlException"><paramref name="formXml"/> isn't well-formed XML at all.</exception>
    public static IReadOnlyList<FormXmlValidationMessage> Validate(string formXml)
    {
        var messages = new List<FormXmlValidationMessage>();

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = SchemaSet.Value,
        };
        settings.ValidationEventHandler += (_, e) => messages.Add(ToValidationMessage(e, formXml));

        using var stringReader = new StringReader(formXml);
        using var xmlReader = XmlReader.Create(stringReader, settings);

        // XmlReader validates as it goes — reading through the whole
        // document is what actually triggers the ValidationEventHandler
        // callbacks above; the loop body itself has nothing to do.
        while (xmlReader.Read())
        {
        }

        return messages;
    }

    private static FormXmlValidationMessage ToValidationMessage(ValidationEventArgs e, string formXml)
    {
        var (line, column) = e.Exception is { LineNumber: > 0 } ex ? (ex.LineNumber, ex.LinePosition) : (0, 0);
        var (snippet, caretOffset) = BuildSnippet(formXml, line, column);
        return new FormXmlValidationMessage(e.Severity, line, column, e.Message, snippet, caretOffset);
    }

    /// <summary>
    /// A short excerpt of <paramref name="formXml"/> around
    /// (<paramref name="lineNumber"/>, <paramref name="linePosition"/>), plus
    /// the offset into that excerpt of the exact character being pointed at —
    /// see <see cref="FormXmlValidationMessage.SnippetCaretOffset"/> for why
    /// that's returned as an offset for the caller to highlight inline,
    /// rather than this method baking in its own second-line caret.
    /// </summary>
    private static (string Snippet, int CaretOffset) BuildSnippet(string formXml, int lineNumber, int linePosition)
    {
        if (lineNumber < 1)
        {
            return ("", 0);
        }

        // XmlException's LineNumber/LinePosition are computed against the
        // reader's own notion of lines, which treats \r\n and \n both as a
        // single line break — matched here so a real position lines up
        // with the right line even if the source has Windows line endings.
        var lines = formXml.Replace("\r\n", "\n").Split('\n');
        if (lineNumber > lines.Length)
        {
            return ("", 0);
        }

        var line = lines[lineNumber - 1];
        var column = Math.Clamp(linePosition - 1, 0, line.Length);

        const int contextChars = 50;
        var start = Math.Max(0, column - contextChars);
        var end = Math.Min(line.Length, column + contextChars);

        var prefix = start > 0 ? "…" : "";
        var suffix = end < line.Length ? "…" : "";
        var excerpt = prefix + line[start..end] + suffix;
        var caretOffset = prefix.Length + (column - start);

        return (excerpt, caretOffset);
    }

    private static XmlSchemaSet LoadSchemaSet()
    {
        var assembly = typeof(FormXmlValidator).Assembly;
        var resolver = new EmbeddedResourceXmlResolver(assembly);

        var schemaSet = new XmlSchemaSet { XmlResolver = resolver };

        using var mainStream = OpenResource(assembly, "FormXml.xsd");
        using var mainReader = XmlReader.Create(mainStream, new XmlReaderSettings { XmlResolver = resolver });
        var mainSchema = XmlSchema.Read(mainReader, validationEventHandler: null)
            ?? throw new InvalidOperationException("Embedded 'FormXml.xsd' failed to parse — this build is broken, not the FormXML being validated.");
        schemaSet.Add(mainSchema);

        schemaSet.Compile();
        return schemaSet;
    }

    private static Stream OpenResource(Assembly assembly, string fileName) =>
        assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"Embedded FormXML schema resource '{fileName}' is missing — this build is broken, not the FormXML being validated.");

    /// <summary>
    /// Resolves each schema's own relative <c>&lt;xs:include schemaLocation="..."&gt;</c>
    /// references (e.g. `FormXml.xsd`'s own reference to `RibbonCore.xsd`,
    /// which in turn references `RibbonTypes.xsd`/`RibbonWSS.xsd`) to the
    /// matching embedded resource instead of the filesystem. These schemas
    /// are embedded into the assembly precisely so this validator keeps
    /// working from the standalone single-file build, where there's no
    /// on-disk `FormXmlSchema` folder to resolve a relative path against at
    /// all — only the leaf filename from each `schemaLocation` is ever used.
    /// </summary>
    private sealed class EmbeddedResourceXmlResolver(Assembly assembly) : XmlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var fileName = Path.GetFileName(absoluteUri.IsAbsoluteUri ? absoluteUri.LocalPath : absoluteUri.OriginalString);
            return OpenResource(assembly, fileName);
        }

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) => new($"embedded:///{relativeUri}");
    }
}

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
/// there's no wildcard attribute to fall back on). That means a violation
/// here isn't necessarily a bug in this tool's own output — callers should
/// surface these as warnings, not a reason to refuse writing the file.
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
    public static IReadOnlyList<string> Validate(string formXml)
    {
        var messages = new List<string>();

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = SchemaSet.Value,
        };
        settings.ValidationEventHandler += (_, e) => messages.Add(FormatMessage(e));

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

    private static string FormatMessage(ValidationEventArgs e) =>
        e.Exception is { LineNumber: > 0 } ex
            ? $"Line {ex.LineNumber}, position {ex.LinePosition}: {e.Message}"
            : e.Message;

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

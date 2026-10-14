using System.Xml;
using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Reads an <see cref="EntityDefinition"/> out of an unpacked solution's
/// <c>Entities/{name}/Entity.xml</c>.
///
/// The parsing below targets the shape `pac solution unpack` produces
/// (an &lt;Entity&gt;/&lt;EntityInfo&gt;/&lt;entity&gt;/&lt;attributes&gt; tree, with
/// per-language display text under LocalizedNames/LocalCollectionNames/Descriptions
/// containers). It reads defensively — optional elements are treated as
/// optional rather than assumed present — but hasn't yet been validated
/// against every attribute type Dynamics can produce (e.g. picklists,
/// lookups); expect to extend <see cref="ParseAttribute"/> as those show up.
/// </summary>
public sealed class EntityXmlDefinitionReader : IEntityDefinitionReader
{
    private const int EnglishLanguageCode = 1033;

    public bool CanRead(string content)
    {
        if (!content.TrimStart().StartsWith('<'))
        {
            return false;
        }

        try
        {
            return XDocument.Parse(content).Root?.Name.LocalName == "Entity";
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public EntityDefinition Read(string content)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Entity.xml is not well-formed XML: {ex.Message}", ex);
        }

        var root = doc.Root
            ?? throw new InvalidDataException("Entity.xml has no root element.");

        var logicalName = (string?)root.Element("Name")
            ?? throw new InvalidDataException("Entity.xml is missing the <Name> element.");

        var entityInfo = root.Element("EntityInfo")?.Element("entity");

        var attributes = entityInfo?.Element("attributes")?.Elements("attribute")
            .Select(ParseAttribute)
            .ToList()
            ?? [];

        return new EntityDefinition
        {
            LogicalName = logicalName,
            DisplayName = FirstLocalizedText(entityInfo?.Element("LocalizedNames"), "LocalizedName"),
            PluralDisplayName = FirstLocalizedText(entityInfo?.Element("LocalCollectionNames"), "LocalCollectionName"),
            Description = FirstLocalizedText(entityInfo?.Element("Descriptions"), "Description"),
            Attributes = attributes,
        };
    }

    private static AttributeDefinition ParseAttribute(XElement attribute)
    {
        var logicalName = (string?)attribute.Element("LogicalName")
            ?? throw new InvalidDataException("An <attribute> is missing its <LogicalName>.");

        var physicalName = (string?)attribute.Attribute("PhysicalName");

        return new AttributeDefinition
        {
            Name = logicalName,
            Type = (string?)attribute.Element("Type") ?? "unknown",
            DisplayName = FirstLocalizedText(attribute.Element("LocalizedNames"), "LocalizedName"),
            Description = FirstLocalizedText(attribute.Element("Descriptions"), "Description"),
            // Only worth keeping when it's not just a mechanical PascalCase of the logical name.
            PhysicalName = physicalName is not null
                && !string.Equals(physicalName, logicalName, StringComparison.OrdinalIgnoreCase)
                ? physicalName
                : null,
            RequiredLevel = DefaultValueConventions.RequiredLevelOrNull((string?)attribute.Element("RequiredLevel")),
            MaxLength = (int?)attribute.Element("MaxLength"),
            IsCustomField = DefaultValueConventions.TrueOrNull((string?)attribute.Element("IsCustomField") == "1"),
            ValidForAdvancedFind = ParseBooleanFlag(attribute.Element("ValidForAdvancedFind")),
        };
    }

    private static bool? ParseBooleanFlag(XElement? element) =>
        element is null ? null : (string?)element == "1";

    /// <summary>
    /// Picks the English (1033) entry from a LocalizedNames/LocalCollectionNames/Descriptions
    /// container, falling back to whichever entry comes first if English isn't present.
    /// </summary>
    private static string? FirstLocalizedText(XElement? container, string childElementName)
    {
        if (container is null)
        {
            return null;
        }

        var entries = container.Elements(childElementName).ToList();

        var english = entries.FirstOrDefault(e => (string?)e.Attribute("languagecode") == EnglishLanguageCode.ToString());
        return (string?)(english ?? entries.FirstOrDefault())?.Attribute("description");
    }
}

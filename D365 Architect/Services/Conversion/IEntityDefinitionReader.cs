using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Strategy for reading an <see cref="EntityDefinition"/> out of one
/// particular source format. Two strategies exist today:
/// <see cref="EntityXmlDefinitionReader"/> (an unpacked solution's
/// Entity.xml) and <see cref="EntityJsonDefinitionReader"/> (live Dataverse
/// Web API EntityDefinitions metadata). Both produce the same curated
/// model, so everything downstream — YAML serialisation, and eventually
/// diffing/apply — is written once against <see cref="EntityDefinition"/>
/// and doesn't care which strategy produced it.
///
/// Forms and views will follow the same shape once implemented: they'll
/// only ever need an XML strategy, since FormXml/FetchXml/LayoutXml are
/// themselves XML documents even when the record wrapping them is fetched
/// as JSON from the Web API.
/// </summary>
public interface IEntityDefinitionReader
{
    /// <summary>True if this reader recognises <paramref name="content"/>'s format.</summary>
    bool CanRead(string content);

    /// <exception cref="InvalidDataException">The content doesn't match the shape this reader expects.</exception>
    EntityDefinition Read(string content);
}

using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Builds the <c>EntityMetadata</c> body <c>POST EntityDefinitions</c> needs
/// to create a table - see
/// https://learn.microsoft.com/power-apps/developer/data-platform/webapi/create-update-entity-definitions-using-web-api
/// ("Create table definitions"). Stub added by the test-first pass; the
/// behaviour is pinned by <c>EntityMetadataJsonBuilderTests</c>.
/// </summary>
public static class EntityMetadataJsonBuilder
{
    /// <exception cref="TableCreateValidationException"><paramref name="entity"/> fails <see cref="TableCreateValidator.Validate"/>.</exception>
    public static JsonObject BuildCreateBody(EntityDefinition entity) => throw new NotImplementedException();
}

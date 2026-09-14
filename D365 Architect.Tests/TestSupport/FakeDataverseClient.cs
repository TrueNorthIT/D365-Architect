using System.Text.Json.Nodes;
using D365Architect.Services.Dataverse;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// A hand-written test double for <see cref="IDataverseClient"/> — no mocking
/// library, matching this codebase's own preference for explicit code over
/// framework magic. Every method not specifically wired up by a test throws
/// <see cref="NotImplementedException"/> rather than silently returning a
/// default, so a test that exercises an unexpected call path fails loudly
/// instead of passing on a bogus null/empty result.
///
/// Read responses are supplied via the <c>*Response</c>/<c>*Json</c> fields;
/// write calls are recorded into the matching <c>*Calls</c> list so a test
/// can assert on exactly what this tool would have sent to Dataverse,
/// without a real HTTP call ever happening.
/// </summary>
public sealed class FakeDataverseClient : IDataverseClient
{
    // ---- Reads (configure the response a test needs) ----
    public string? EntityDefinitionJson { get; set; }
    public string? EntityMetadataJson { get; set; }
    public Func<string, string, string>? AttributeMetadataJson { get; set; } // (attributeLogicalName, attributeType) -> json
    public Func<string, string, string>? AttributeOptionSetJson { get; set; } // (attributeLogicalName, attributeType) -> json
    public Func<string, string?>? GlobalOptionSetJsonByName { get; set; } // name -> json, null = not found
    public string? GlobalOptionSetsJson { get; set; }
    public Func<string, IReadOnlySet<Guid>?>? SolutionAttributeMetadataIds { get; set; }
    public Func<string, IReadOnlySet<Guid>?>? SolutionOptionSetMetadataIds { get; set; }

    // ---- Writes (inspect after the call) ----
    public List<(string EntityLogicalName, JsonObject Body)> CreateAttributeCalls { get; } = [];
    public List<(string EntityLogicalName, string AttributeLogicalName, JsonObject Body)> UpdateAttributeCalls { get; } = [];
    public List<JsonObject> CreateOneToManyRelationshipCalls { get; } = [];
    public List<JsonObject> CreateCustomerRelationshipsCalls { get; } = [];
    public List<(string EntityLogicalName, JsonObject Body)> UpdateEntityCalls { get; } = [];
    public List<JsonObject> InsertOptionValueCalls { get; } = [];
    public List<JsonObject> UpdateOptionValueCalls { get; } = [];
    public List<JsonObject> OrderOptionsCalls { get; } = [];
    public List<JsonObject> InsertStatusValueCalls { get; } = [];
    public List<JsonObject> UpdateStateValueCalls { get; } = [];
    public List<JsonObject> CreateGlobalOptionSetCalls { get; } = [];
    public List<(Guid MetadataId, JsonObject Body)> UpdateGlobalOptionSetCalls { get; } = [];
    public List<(Guid FormId, string FormXml)> UpdateSystemFormXmlCalls { get; } = [];
    public List<string> PublishEntityCalls { get; } = [];

    public Task<WhoAmIResult> WhoAmIAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string?> TryGetUserFullNameAsync(Uri environmentUrl, string accessToken, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<string> GetEntityDefinitionJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) =>
        Task.FromResult(EntityDefinitionJson ?? throw new InvalidOperationException($"{nameof(EntityDefinitionJson)} wasn't configured."));

    public Task<IReadOnlySet<Guid>?> TryGetSolutionAttributeMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) =>
        Task.FromResult(SolutionAttributeMetadataIds is not null ? SolutionAttributeMetadataIds(solutionUniqueName) : throw new NotImplementedException());

    public Task<string> GetViewDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IReadOnlySet<Guid>?> TryGetSolutionSavedQueryIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) => throw new NotImplementedException();

    public ExistingSavedQuery? SavedQuery { get; set; }
    public List<(Guid SavedQueryId, string? Description, string? FetchXml, string? LayoutXml)> UpdateSavedQueryCalls { get; } = [];

    public Task<ExistingSavedQuery?> TryGetSavedQueryAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string viewName, CancellationToken cancellationToken) =>
        Task.FromResult(SavedQuery);

    public Task UpdateSavedQueryAsync(Uri environmentUrl, string accessToken, Guid savedQueryId, string? description, string? fetchXml, string? layoutXml, CancellationToken cancellationToken)
    {
        UpdateSavedQueryCalls.Add((savedQueryId, description, fetchXml, layoutXml));
        return Task.CompletedTask;
    }
    public Task<string> GetFormDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string> GetFormSummariesJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IReadOnlySet<Guid>?> TryGetSolutionSystemFormIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string?> TryGetSystemFormXmlAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ExistingSystemForm?> TryGetSystemFormAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<ExistingSystemForm?> TryGetSystemFormByIdAsync(Uri environmentUrl, string accessToken, Guid formId, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task UpdateSystemFormXmlAsync(Uri environmentUrl, string accessToken, Guid formId, string formXml, CancellationToken cancellationToken)
    {
        UpdateSystemFormXmlCalls.Add((formId, formXml));
        return Task.CompletedTask;
    }

    public Task PublishEntityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        PublishEntityCalls.Add(entityLogicalName);
        return Task.CompletedTask;
    }

    public Task<string> GetEntityMetadataJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) =>
        Task.FromResult(EntityMetadataJson ?? throw new InvalidOperationException($"{nameof(EntityMetadataJson)} wasn't configured."));

    public Task UpdateEntityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject entityMetadata, CancellationToken cancellationToken)
    {
        UpdateEntityCalls.Add((entityLogicalName, entityMetadata));
        return Task.CompletedTask;
    }

    public Task<string> GetAttributeMetadataJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken) =>
        Task.FromResult(AttributeMetadataJson is not null ? AttributeMetadataJson(attributeLogicalName, attributeType) : throw new NotImplementedException());

    public Task UpdateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken)
    {
        UpdateAttributeCalls.Add((entityLogicalName, attributeLogicalName, attributeMetadata));
        return Task.CompletedTask;
    }

    public Task CreateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken)
    {
        CreateAttributeCalls.Add((entityLogicalName, attributeMetadata));
        return Task.CompletedTask;
    }

    public Task<string> GetAttributeOptionSetJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken) =>
        Task.FromResult(AttributeOptionSetJson is not null ? AttributeOptionSetJson(attributeLogicalName, attributeType) : throw new NotImplementedException());

    public Task CreateOneToManyRelationshipAsync(Uri environmentUrl, string accessToken, JsonObject relationshipMetadata, CancellationToken cancellationToken)
    {
        CreateOneToManyRelationshipCalls.Add(relationshipMetadata);
        return Task.CompletedTask;
    }

    public Task CreateCustomerRelationshipsAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        CreateCustomerRelationshipsCalls.Add(body);
        return Task.CompletedTask;
    }

    public Task InsertOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        InsertOptionValueCalls.Add(body);
        return Task.CompletedTask;
    }

    public Task UpdateOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        UpdateOptionValueCalls.Add(body);
        return Task.CompletedTask;
    }

    public Task DeleteOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task OrderOptionsAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        OrderOptionsCalls.Add(body);
        return Task.CompletedTask;
    }

    public Task InsertStatusValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        InsertStatusValueCalls.Add(body);
        return Task.CompletedTask;
    }

    public Task UpdateStateValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        UpdateStateValueCalls.Add(body);
        return Task.CompletedTask;
    }

    public Task<string?> TryGetGlobalOptionSetJsonAsync(Uri environmentUrl, string accessToken, string name, CancellationToken cancellationToken) =>
        Task.FromResult(GlobalOptionSetJsonByName is not null ? GlobalOptionSetJsonByName(name) : throw new NotImplementedException());

    public Task<string> GetGlobalOptionSetsJsonAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult(GlobalOptionSetsJson ?? throw new InvalidOperationException($"{nameof(GlobalOptionSetsJson)} wasn't configured."));

    public Task<IReadOnlySet<Guid>?> TryGetSolutionOptionSetMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) =>
        Task.FromResult(SolutionOptionSetMetadataIds is not null ? SolutionOptionSetMetadataIds(solutionUniqueName) : throw new NotImplementedException());

    public Task CreateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        CreateGlobalOptionSetCalls.Add(body);
        return Task.CompletedTask;
    }

    public Task UpdateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, Guid metadataId, JsonObject body, CancellationToken cancellationToken)
    {
        UpdateGlobalOptionSetCalls.Add((metadataId, body));
        return Task.CompletedTask;
    }
}

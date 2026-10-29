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
    public Func<string, IReadOnlyList<string>?>? SolutionEntityLogicalNames { get; set; }
    public Func<string, string, bool>? SolutionEntityIncludesSubcomponents { get; set; } // (solutionUniqueName, entityLogicalName) -> bool

    /// <summary>Answers <see cref="EntityExistsAsync"/>: logical name -> exists live. Unset throws, like every other unconfigured read.</summary>
    public Func<string, bool>? EntityExists { get; set; }

    /// <summary>Every logical name <see cref="EntityExistsAsync"/> was asked about, in order.</summary>
    public List<string> EntityExistsCalls { get; } = [];

    /// <summary>When set, <see cref="CreateEntityAsync"/> (and a <see cref="DataverseWrite.CreateEntity"/> in a transaction) throws this instead of recording the call - simulates Dataverse rejecting the create.</summary>
    public Exception? CreateEntityFailure { get; set; }

    // ---- Writes (inspect after the call) ----
    public List<(JsonObject Body, string? SolutionUniqueName)> CreateEntityCalls { get; } = [];
    public List<(string EntityLogicalName, JsonObject Body, string? SolutionUniqueName)> CreateAttributeCalls { get; } = [];
    public List<(string EntityLogicalName, string AttributeLogicalName, JsonObject Body)> UpdateAttributeCalls { get; } = [];
    public List<(JsonObject Metadata, string? SolutionUniqueName)> CreateOneToManyRelationshipCalls { get; } = [];
    public List<(JsonObject Body, string? SolutionUniqueName)> CreateCustomerRelationshipsCalls { get; } = [];
    public List<(string EntityLogicalName, JsonObject Body)> UpdateEntityCalls { get; } = [];
    public List<JsonObject> InsertOptionValueCalls { get; } = [];
    public List<JsonObject> UpdateOptionValueCalls { get; } = [];
    public List<JsonObject> OrderOptionsCalls { get; } = [];
    public List<JsonObject> InsertStatusValueCalls { get; } = [];
    public List<JsonObject> UpdateStateValueCalls { get; } = [];
    public List<(JsonObject Body, string? SolutionUniqueName)> CreateGlobalOptionSetCalls { get; } = [];
    public List<(Guid MetadataId, JsonObject Body)> UpdateGlobalOptionSetCalls { get; } = [];
    public List<(Guid FormId, string FormXml, string? SolutionUniqueName)> UpdateSystemFormXmlCalls { get; } = [];
    public List<string> PublishEntityCalls { get; } = [];
    public List<(string EntityLogicalName, string AttributeLogicalName, int Value)> SetAutoNumberSeedCalls { get; } = [];

    /// <summary>
    /// Column creates and seed writes in the order they were issued (e.g.
    /// <c>create:tn_Ref</c>, <c>seed:tn_ref=1</c>), across both the
    /// individual-call and transaction paths - what an ordering test reads.
    /// </summary>
    public List<string> ColumnWriteOrder { get; } = [];

    public Task<WhoAmIResult> WhoAmIAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string?> TryGetUserFullNameAsync(Uri environmentUrl, string accessToken, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<string> GetEntityDefinitionJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) =>
        Task.FromResult(EntityDefinitionJson ?? throw new InvalidOperationException($"{nameof(EntityDefinitionJson)} wasn't configured."));

    public Task<bool> EntityExistsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        EntityExistsCalls.Add(entityLogicalName);
        return Task.FromResult(EntityExists is not null ? EntityExists(entityLogicalName) : throw new NotImplementedException());
    }

    public Task CreateEntityAsync(Uri environmentUrl, string accessToken, JsonObject entityMetadata, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        if (CreateEntityFailure is not null)
        {
            throw CreateEntityFailure;
        }

        CreateEntityCalls.Add((entityMetadata, solutionUniqueName));
        WriteOrder.Add($"create-table:{(string?)entityMetadata["SchemaName"]}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlySet<Guid>?> TryGetSolutionAttributeMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) =>
        Task.FromResult(SolutionAttributeMetadataIds is not null ? SolutionAttributeMetadataIds(solutionUniqueName) : throw new NotImplementedException());

    public Task<IReadOnlyList<string>?> TryGetSolutionEntityLogicalNamesAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) =>
        Task.FromResult(SolutionEntityLogicalNames is not null ? SolutionEntityLogicalNames(solutionUniqueName) : throw new NotImplementedException());

    public Task<bool> IsSolutionEntityIncludingSubcomponentsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, string entityLogicalName, CancellationToken cancellationToken) =>
        Task.FromResult(SolutionEntityIncludesSubcomponents is not null ? SolutionEntityIncludesSubcomponents(solutionUniqueName, entityLogicalName) : throw new NotImplementedException());

    public string? ViewDefinitionsJson { get; set; }

    public Task<string> GetViewDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) =>
        Task.FromResult(ViewDefinitionsJson ?? throw new InvalidOperationException($"{nameof(ViewDefinitionsJson)} wasn't configured."));

    public Task<IReadOnlySet<Guid>?> TryGetSolutionSavedQueryIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) => throw new NotImplementedException();

    public ExistingSavedQuery? SavedQuery { get; set; }
    public List<(Guid SavedQueryId, string? Description, string? FetchXml, string? LayoutXml, string? SolutionUniqueName)> UpdateSavedQueryCalls { get; } = [];

    /// <summary>
    /// Live views keyed by <c>savedqueryid</c> - what
    /// <see cref="TryGetSavedQueryByIdAsync"/> resolves against. A missing key
    /// is "not found" (null), the same as a real lookup of an id that
    /// doesn't exist.
    /// </summary>
    public Dictionary<Guid, ExistingSavedQuery> SavedQueriesById { get; } = [];

    /// <summary>
    /// When true, the by-name lookup (<see cref="TryGetSavedQueryAsync"/>)
    /// behaves as if two live views shared that table + name - it throws
    /// <see cref="AmbiguousSavedQueryException"/>, exactly as the real client
    /// does - regardless of <see cref="SavedQuery"/>.
    /// </summary>
    public bool SavedQueryNameIsAmbiguous { get; set; }

    public Task<ExistingSavedQuery?> TryGetSavedQueryAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string viewName, CancellationToken cancellationToken) =>
        SavedQueryNameIsAmbiguous
            ? throw new AmbiguousSavedQueryException(entityLogicalName, viewName, [Guid.NewGuid(), Guid.NewGuid()])
            : Task.FromResult(SavedQuery);

    public Task<ExistingSavedQuery?> TryGetSavedQueryByIdAsync(Uri environmentUrl, string accessToken, Guid savedQueryId, CancellationToken cancellationToken) =>
        Task.FromResult(SavedQueriesById.GetValueOrDefault(savedQueryId));

    public Task UpdateSavedQueryAsync(Uri environmentUrl, string accessToken, Guid savedQueryId, string? description, string? fetchXml, string? layoutXml, CancellationToken cancellationToken) =>
        UpdateSavedQueryAsync(environmentUrl, accessToken, savedQueryId, description, fetchXml, layoutXml, solutionUniqueName: null, cancellationToken);

    public Task UpdateSavedQueryAsync(Uri environmentUrl, string accessToken, Guid savedQueryId, string? description, string? fetchXml, string? layoutXml, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        UpdateSavedQueryCalls.Add((savedQueryId, description, fetchXml, layoutXml, solutionUniqueName));
        WriteOrder.Add($"update-view:{savedQueryId}");
        return Task.CompletedTask;
    }
    // ---- Simpler hand-written forms and views (REQ-22 to REQ-33) ----

    /// <summary>Every create/update of a table, view or form, in the order issued, e.g. <c>create-table:tn_Project</c>, <c>create-view:All projects</c>, <c>create-form:Project</c> - what the one-run ordering test reads.</summary>
    public List<string> WriteOrder { get; } = [];

    /// <summary>Live table identities by logical name - what <see cref="TryGetEntityIdentityAsync"/> answers. A missing key is "no such table" (null), as a real 404 is.</summary>
    public Dictionary<string, EntityIdentity> EntityIdentities { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every logical name <see cref="TryGetEntityIdentityAsync"/> was asked about.</summary>
    public List<string> EntityIdentityCalls { get; } = [];

    public Task<EntityIdentity?> TryGetEntityIdentityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        EntityIdentityCalls.Add(entityLogicalName);
        return Task.FromResult(EntityIdentities.GetValueOrDefault(entityLogicalName));
    }

    public List<(JsonObject Body, string? SolutionUniqueName)> CreateSavedQueryCalls { get; } = [];

    public Task CreateSavedQueryAsync(Uri environmentUrl, string accessToken, JsonObject body, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        CreateSavedQueryCalls.Add((body, solutionUniqueName));
        WriteOrder.Add($"create-view:{(string?)body["name"]}");
        return Task.CompletedTask;
    }

    public List<(JsonObject Body, string? SolutionUniqueName)> CreateSystemFormCalls { get; } = [];

    public Task CreateSystemFormAsync(Uri environmentUrl, string accessToken, JsonObject body, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        CreateSystemFormCalls.Add((body, solutionUniqueName));
        WriteOrder.Add($"create-form:{(string?)body["name"]}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Live forms with their systemform <c>type</c> - what <see cref="FindSystemFormsAsync"/> filters, on
    /// table (<see cref="ExistingSystemForm.EntityLogicalName"/>), name (<see cref="ExistingSystemForm.Name"/>) and type,
    /// as the real query does. Left empty, it falls back to <see cref="SystemForm"/> (any type) so older tests keep working.
    /// </summary>
    public List<(ExistingSystemForm Form, int Type)> LiveSystemForms { get; } = [];

    /// <summary>Every (table, name, type) <see cref="FindSystemFormsAsync"/> was asked for.</summary>
    public List<(string EntityLogicalName, string FormName, int FormType)> FindSystemFormsCalls { get; } = [];

    public Task<IReadOnlyList<ExistingSystemForm>> FindSystemFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, int formType, CancellationToken cancellationToken)
    {
        FindSystemFormsCalls.Add((entityLogicalName, formName, formType));

        IReadOnlyList<ExistingSystemForm> found = LiveSystemForms.Count > 0 || SystemForm is null
            ? LiveSystemForms
                .Where(f => string.Equals(f.Form.EntityLogicalName, entityLogicalName, StringComparison.OrdinalIgnoreCase) && f.Form.Name == formName && f.Type == formType)
                .Select(f => f.Form)
                .ToList()
            : [SystemForm];
        return Task.FromResult(found);
    }

    public Task<string> GetFormDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string> GetFormSummariesJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<IReadOnlySet<Guid>?> TryGetSolutionSystemFormIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string?> TryGetSystemFormXmlAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken) => throw new NotImplementedException();

    /// <summary>The live form both <see cref="TryGetSystemFormAsync"/> and <see cref="TryGetSystemFormByIdAsync"/> return (null = not found).</summary>
    public ExistingSystemForm? SystemForm { get; set; }

    public Task<ExistingSystemForm?> TryGetSystemFormAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken) =>
        Task.FromResult(SystemForm);

    public Task<ExistingSystemForm?> TryGetSystemFormByIdAsync(Uri environmentUrl, string accessToken, Guid formId, CancellationToken cancellationToken) =>
        Task.FromResult(SystemForm);

    public Task UpdateSystemFormXmlAsync(Uri environmentUrl, string accessToken, Guid formId, string formXml, CancellationToken cancellationToken) =>
        UpdateSystemFormXmlAsync(environmentUrl, accessToken, formId, formXml, solutionUniqueName: null, cancellationToken);

    public Task UpdateSystemFormXmlAsync(Uri environmentUrl, string accessToken, Guid formId, string formXml, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        UpdateSystemFormXmlCalls.Add((formId, formXml, solutionUniqueName));
        WriteOrder.Add($"update-form:{formId}");
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

    public Task CreateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject attributeMetadata, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        CreateAttributeCalls.Add((entityLogicalName, attributeMetadata, solutionUniqueName));
        ColumnWriteOrder.Add($"create:{(string?)attributeMetadata["SchemaName"]}");
        return Task.CompletedTask;
    }

    public Task SetAutoNumberSeedAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, int value, CancellationToken cancellationToken)
    {
        SetAutoNumberSeedCalls.Add((entityLogicalName, attributeLogicalName, value));
        ColumnWriteOrder.Add($"seed:{attributeLogicalName}={value}");
        return Task.CompletedTask;
    }

    public Task<string> GetAttributeOptionSetJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken) =>
        Task.FromResult(AttributeOptionSetJson is not null ? AttributeOptionSetJson(attributeLogicalName, attributeType) : throw new NotImplementedException());

    public Task CreateOneToManyRelationshipAsync(Uri environmentUrl, string accessToken, JsonObject relationshipMetadata, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        CreateOneToManyRelationshipCalls.Add((relationshipMetadata, solutionUniqueName));
        return Task.CompletedTask;
    }

    public Task CreateCustomerRelationshipsAsync(Uri environmentUrl, string accessToken, JsonObject body, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        CreateCustomerRelationshipsCalls.Add((body, solutionUniqueName));
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

    public Task CreateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, JsonObject body, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        CreateGlobalOptionSetCalls.Add((body, solutionUniqueName));
        return Task.CompletedTask;
    }

    public Task UpdateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, Guid metadataId, JsonObject body, CancellationToken cancellationToken)
    {
        UpdateGlobalOptionSetCalls.Add((metadataId, body));
        return Task.CompletedTask;
    }

    // ---- ExecuteTransactionAsync: dispatches into the exact same *Calls
    // lists as the individual write methods above, so a test written
    // against the sequential (useTransaction: false) path keeps working
    // unchanged when it instead exercises the batched/transactional one —
    // see TableImportServiceTests' "ApplyAsync wiring" section, which calls
    // the plain four-argument ApplyAsync (transaction-by-default) but only
    // ever asserts against these lists. No real batching/rollback semantics
    // here; this fake only needs to record what was asked for. ----
    public List<IReadOnlyList<DataverseWrite>> ExecuteTransactionCalls { get; } = [];

    public Task ExecuteTransactionAsync(Uri environmentUrl, string accessToken, IReadOnlyList<DataverseWrite> writes, CancellationToken cancellationToken)
    {
        ExecuteTransactionCalls.Add(writes);

        foreach (var write in writes)
        {
            switch (write)
            {
                case DataverseWrite.CreateEntity w:
                    if (CreateEntityFailure is not null)
                    {
                        throw CreateEntityFailure;
                    }

                    CreateEntityCalls.Add((w.Metadata, w.SolutionUniqueName));
                    WriteOrder.Add($"create-table:{(string?)w.Metadata["SchemaName"]}");
                    break;

                case DataverseWrite.UpdateEntity w:
                    UpdateEntityCalls.Add((w.EntityLogicalName, w.Metadata));
                    break;

                case DataverseWrite.CreateAttribute w:
                    CreateAttributeCalls.Add((w.EntityLogicalName, w.Metadata, w.SolutionUniqueName));
                    ColumnWriteOrder.Add($"create:{(string?)w.Metadata["SchemaName"]}");
                    break;

                case DataverseWrite.SetAutoNumberSeed w:
                    SetAutoNumberSeedCalls.Add((w.EntityLogicalName, w.AttributeLogicalName, w.Value));
                    ColumnWriteOrder.Add($"seed:{w.AttributeLogicalName}={w.Value}");
                    break;

                case DataverseWrite.UpdateAttribute w:
                    UpdateAttributeCalls.Add((w.EntityLogicalName, w.AttributeLogicalName, w.Metadata));
                    break;

                case DataverseWrite.CreateOneToManyRelationship w:
                    CreateOneToManyRelationshipCalls.Add((w.Metadata, w.SolutionUniqueName));
                    break;

                case DataverseWrite.CreateCustomerRelationships w:
                    CreateCustomerRelationshipsCalls.Add((w.Body, w.SolutionUniqueName));
                    break;

                case DataverseWrite.InsertOptionValue w:
                    InsertOptionValueCalls.Add(w.Body);
                    break;

                case DataverseWrite.UpdateOptionValue w:
                    UpdateOptionValueCalls.Add(w.Body);
                    break;

                case DataverseWrite.OrderOptions w:
                    OrderOptionsCalls.Add(w.Body);
                    break;

                case DataverseWrite.InsertStatusValue w:
                    InsertStatusValueCalls.Add(w.Body);
                    break;

                case DataverseWrite.UpdateStateValue w:
                    UpdateStateValueCalls.Add(w.Body);
                    break;
            }
        }

        return Task.CompletedTask;
    }
}

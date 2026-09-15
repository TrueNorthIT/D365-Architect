using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace D365Architect.Services.Dataverse;

public sealed class DataverseClient(HttpClient httpClient) : IDataverseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // System.Net.Http.Json's JsonContent.Create camelCases property names by
    // default when no options are given (confirmed live: {"ParameterXml": ...}
    // came out on the wire as {"parameterXml": ...}, which Dataverse's
    // PublishXml action then rejected as an unrecognised parameter — Dataverse
    // Web API property/parameter names are case-sensitive and not
    // consistently camelCase, e.g. PublishXmlRequest.ParameterXml is PascalCase
    // while systemforms' own formxml attribute is already all-lowercase).
    // Anonymous-object request bodies need their member names sent exactly as
    // written, so they go through this instead of the implicit default.
    private static readonly JsonSerializerOptions VerbatimJsonOptions = new();

    public async Task<WhoAmIResult> WhoAmIAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(environmentUrl, "WhoAmI", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<WhoAmIResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Dataverse returned an empty WhoAmI response.");

        return new WhoAmIResult(payload.UserId, payload.BusinessUnitId, payload.OrganizationId);
    }

    public async Task<string?> TryGetUserFullNameAsync(Uri environmentUrl, string accessToken, Guid userId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(environmentUrl, $"systemusers({userId})?$select=fullname", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<SystemUserResponse>(JsonOptions, cancellationToken);
        return payload?.FullName;
    }

    public async Task<string> GetEntityDefinitionJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        // Deliberately no $select on Attributes: it's a collection of the base
        // AttributeMetadata type, so $select can only name properties that
        // exist on every attribute regardless of type — type-specific ones
        // (MaxLength on strings, Precision on money, Targets on lookups, ...)
        // aren't selectable there and make the whole request fail with a 400
        // if named. Taking the full object per attribute is the only way to
        // get at those without knowing each attribute's concrete type upfront.
        var relativePath = $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')" +
            "?$select=LogicalName,SchemaName,DisplayName,DisplayCollectionName,Description," +
            "OwnershipType,IsActivity,HasActivities,HasNotes" +
            "&$expand=Attributes";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        request.Headers.Add("Prefer", "odata.include-annotations=\"*\"");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>?> TryGetSolutionAttributeMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken)
    {
        var solutionId = await TryGetSolutionIdAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken);
        if (solutionId is null)
        {
            return null;
        }

        // componenttype 2 = Attribute. Confirmed empirically against a real
        // tenant's solutioncomponents (1 = Entity, 2 = Attribute) rather than
        // trusted from memory alone — see the SDK's componenttype OptionSet
        // for the full list if more component types are needed later.
        const int attributeComponentType = 2;
        return await GetSolutionComponentObjectIdsAsync(environmentUrl, accessToken, solutionId.Value, attributeComponentType, cancellationToken);
    }

    public async Task<string> GetViewDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        var relativePath = "savedqueries?$select=savedqueryid,name,description,fetchxml,layoutxml," +
            "querytype,returnedtypecode,isdefault,isquickfindquery,isuserdefined,iscustomizable" +
            $"&$filter=returnedtypecode eq '{Uri.EscapeDataString(entityLogicalName)}'&$orderby=name";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>?> TryGetSolutionSavedQueryIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken)
    {
        var solutionId = await TryGetSolutionIdAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken);
        if (solutionId is null)
        {
            return null;
        }

        // componenttype 26 = View (the SavedQuery entity). Per Microsoft's
        // documented solutioncomponent componenttype option set:
        // https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/solutioncomponent
        const int savedQueryComponentType = 26;
        return await GetSolutionComponentObjectIdsAsync(environmentUrl, accessToken, solutionId.Value, savedQueryComponentType, cancellationToken);
    }

    public async Task<string> GetFormDefinitionsJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        var relativePath = "systemforms?$select=formid,name,description,type,objecttypecode,formxml," +
            "isdefault,formactivationstate,iscustomizable" +
            $"&$filter=objecttypecode eq '{Uri.EscapeDataString(entityLogicalName)}'&$orderby=name";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetFormSummariesJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        var relativePath = "systemforms?$select=formid,name,type" +
            $"&$filter=objecttypecode eq '{Uri.EscapeDataString(entityLogicalName)}'&$orderby=name";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>?> TryGetSolutionSystemFormIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken)
    {
        var solutionId = await TryGetSolutionIdAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken);
        if (solutionId is null)
        {
            return null;
        }

        // componenttype 60 = System Form. Per Microsoft's documented
        // solutioncomponent componenttype option set:
        // https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/solutioncomponent
        const int systemFormComponentType = 60;
        return await GetSolutionComponentObjectIdsAsync(environmentUrl, accessToken, solutionId.Value, systemFormComponentType, cancellationToken);
    }

    public async Task<string?> TryGetSystemFormXmlAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken)
    {
        // Unlike every other filter in this file, `formName` is a
        // human-editable display name rather than a logical/unique name, so
        // it can genuinely contain an apostrophe (e.g. "Editor's View") —
        // OData string literals need that doubled, not just URL-escaped.
        var relativePath = "systemforms?$select=formxml" +
            $"&$filter=objecttypecode eq '{Uri.EscapeDataString(entityLogicalName)}' and name eq '{Uri.EscapeDataString(EscapeODataStringLiteral(formName))}'";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("value").EnumerateArray().ToList();

        if (results.Count == 0)
        {
            return null;
        }

        if (results.Count > 1)
        {
            throw new AmbiguousSystemFormException(entityLogicalName, formName, results.Count);
        }

        return results[0].GetProperty("formxml").GetString();
    }

    public async Task<ExistingSystemForm?> TryGetSystemFormAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string formName, CancellationToken cancellationToken)
    {
        var relativePath = "systemforms?$select=formid,formxml" +
            $"&$filter=objecttypecode eq '{Uri.EscapeDataString(entityLogicalName)}' and name eq '{Uri.EscapeDataString(EscapeODataStringLiteral(formName))}'";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("value").EnumerateArray().ToList();

        if (results.Count == 0)
        {
            return null;
        }

        if (results.Count > 1)
        {
            throw new AmbiguousSystemFormException(entityLogicalName, formName, results.Count);
        }

        var formId = results[0].GetProperty("formid").GetGuid();
        var formXml = results[0].GetProperty("formxml").GetString() ?? "";
        return new ExistingSystemForm(formId, formXml);
    }

    public async Task<ExistingSystemForm?> TryGetSystemFormByIdAsync(Uri environmentUrl, string accessToken, Guid formId, CancellationToken cancellationToken)
    {
        var relativePath = $"systemforms({formId})?$select=formid,formxml,objecttypecode,name";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var formXml = doc.RootElement.GetProperty("formxml").GetString() ?? "";
        var entityLogicalName = doc.RootElement.GetProperty("objecttypecode").GetString() ?? "";
        var name = doc.RootElement.GetProperty("name").GetString() ?? "";
        return new ExistingSystemForm(formId, formXml, entityLogicalName, name);
    }

    public async Task UpdateSystemFormXmlAsync(Uri environmentUrl, string accessToken, Guid formId, string formXml, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(environmentUrl, $"systemforms({formId})", accessToken, HttpMethod.Patch);
        request.Content = JsonContent.Create(new { formxml = formXml }, options: VerbatimJsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task PublishEntityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        // Schema confirmed against Microsoft's own docs for
        // PublishXmlRequest.ParameterXml: <entities><entity> takes the
        // table's logical name and publishes everything on it (forms,
        // views, ribbons, attributes) — there's no documented way to name
        // just one systemform within it.
        var parameterXml = new XElement("importexportxml",
            new XElement("entities",
                new XElement("entity", entityLogicalName))).ToString(SaveOptions.DisableFormatting);

        using var request = CreateRequest(environmentUrl, "PublishXml", accessToken, HttpMethod.Post);
        request.Content = JsonContent.Create(new { ParameterXml = parameterXml }, options: VerbatimJsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ExistingSavedQuery?> TryGetSavedQueryAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string viewName, CancellationToken cancellationToken)
    {
        var relativePath = "savedqueries?$select=savedqueryid,description,fetchxml,layoutxml" +
            $"&$filter=returnedtypecode eq '{Uri.EscapeDataString(entityLogicalName)}' and name eq '{Uri.EscapeDataString(EscapeODataStringLiteral(viewName))}'";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("value").EnumerateArray().ToList();

        if (results.Count == 0)
        {
            return null;
        }

        if (results.Count > 1)
        {
            throw new AmbiguousSavedQueryException(entityLogicalName, viewName, results.Count);
        }

        var result = results[0];
        var savedQueryId = result.GetProperty("savedqueryid").GetGuid();
        var description = result.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        var fetchXml = result.TryGetProperty("fetchxml", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        var layoutXml = result.TryGetProperty("layoutxml", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
        return new ExistingSavedQuery(savedQueryId, description, fetchXml, layoutXml);
    }

    public async Task UpdateSavedQueryAsync(Uri environmentUrl, string accessToken, Guid savedQueryId, string? description, string? fetchXml, string? layoutXml, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>();
        if (description is not null)
        {
            body["description"] = description;
        }

        if (fetchXml is not null)
        {
            body["fetchxml"] = fetchXml;
        }

        if (layoutXml is not null)
        {
            body["layoutxml"] = layoutXml;
        }

        if (body.Count == 0)
        {
            return;
        }

        using var request = CreateRequest(environmentUrl, $"savedqueries({savedQueryId})", accessToken, HttpMethod.Patch);
        request.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<string> GetEntityMetadataJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, CancellationToken cancellationToken)
    {
        var relativePath = $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')" +
            "?$select=LogicalName,SchemaName,DisplayName,DisplayCollectionName,Description," +
            "OwnershipType,IsActivity,HasActivities,HasNotes";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task UpdateEntityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject entityMetadata, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.UpdateEntity(entityLogicalName, entityMetadata), cancellationToken);

    public async Task<string> GetAttributeMetadataJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken)
    {
        var relativePath = $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')" +
            $"/Attributes(LogicalName='{Uri.EscapeDataString(attributeLogicalName)}')" +
            $"/Microsoft.Dynamics.CRM.{attributeType}AttributeMetadata";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // No type-cast segment on the update URL, unlike the GET -- confirmed
    // against Microsoft's own documented example: the type comes from the
    // body's own "@odata.type", not the URL.
    public async Task UpdateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.UpdateAttribute(entityLogicalName, attributeLogicalName, attributeMetadata), cancellationToken);

    public async Task CreateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.CreateAttribute(entityLogicalName, attributeMetadata), cancellationToken);

    public async Task<string> GetAttributeOptionSetJsonAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, string attributeType, CancellationToken cancellationToken)
    {
        var relativePath = $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')" +
            $"/Attributes(LogicalName='{Uri.EscapeDataString(attributeLogicalName)}')" +
            $"/Microsoft.Dynamics.CRM.{attributeType}AttributeMetadata" +
            "?$expand=OptionSet,GlobalOptionSet";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task CreateOneToManyRelationshipAsync(Uri environmentUrl, string accessToken, JsonObject relationshipMetadata, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.CreateOneToManyRelationship(relationshipMetadata), cancellationToken);

    public async Task CreateCustomerRelationshipsAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.CreateCustomerRelationships(body), cancellationToken);

    public async Task InsertOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.InsertOptionValue(body), cancellationToken);

    public async Task UpdateOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.UpdateOptionValue(body), cancellationToken);

    public async Task DeleteOptionValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) =>
        await PostActionAsync(environmentUrl, "DeleteOptionValue", accessToken, body, cancellationToken);

    public async Task OrderOptionsAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.OrderOptions(body), cancellationToken);

    public async Task InsertStatusValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.InsertStatusValue(body), cancellationToken);

    public async Task UpdateStateValueAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken) =>
        await SendWriteAsync(environmentUrl, accessToken, new DataverseWrite.UpdateStateValue(body), cancellationToken);

    /// <summary>
    /// Shared by every simple "POST an action, ignore the response body"
    /// call above that isn't (yet) a <see cref="DataverseWrite"/> case — same
    /// shape as <see cref="PublishEntityAsync"/>'s own request/response
    /// handling. Only <see cref="DeleteOptionValueAsync"/> still goes through
    /// this directly: it's never actually called (see its own doc comment on
    /// <see cref="IDataverseClient"/>), so it was left out of the
    /// <see cref="ExecuteTransactionAsync"/> batching path rather than
    /// growing a union case nothing exercises.
    /// </summary>
    private async Task PostActionAsync(Uri environmentUrl, string actionName, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(environmentUrl, actionName, accessToken, HttpMethod.Post);
        request.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Sends one <see cref="DataverseWrite"/> as its own ordinary HTTP request — what every individual write method above now delegates to, sharing <see cref="ToHttpRequest"/> with <see cref="ExecuteTransactionAsync"/> so the two paths can never drift apart on URL/header shape.</summary>
    private async Task SendWriteAsync(Uri environmentUrl, string accessToken, DataverseWrite write, CancellationToken cancellationToken)
    {
        var (method, relativeUrl, body, headerName, headerValue) = ToHttpRequest(write);

        using var request = CreateRequest(environmentUrl, relativeUrl, accessToken, method);
        if (headerName is not null)
        {
            request.Headers.Add(headerName, headerValue);
        }

        request.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task ExecuteTransactionAsync(Uri environmentUrl, string accessToken, IReadOnlyList<DataverseWrite> writes, CancellationToken cancellationToken)
    {
        if (writes.Count == 0)
        {
            return;
        }

        var batchBoundary = $"batch_{Guid.NewGuid()}";
        var changesetBoundary = $"changeset_{Guid.NewGuid()}";
        var requestBody = BuildTransactionRequestBody(batchBoundary, changesetBoundary, writes);

        using var request = CreateRequest(environmentUrl, "$batch", accessToken, HttpMethod.Post);
        request.Content = new StringContent(requestBody, Encoding.UTF8);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/mixed; boundary=\"{batchBoundary}\"");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Dataverse transaction failed ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
        }

        var responseBoundary = response.Content.Headers.ContentType?.Parameters
            .FirstOrDefault(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"');
        if (responseBoundary is null)
        {
            throw new HttpRequestException($"Dataverse's $batch response had no multipart boundary to parse: {responseBody}");
        }

        var parts = ODataBatchResponseParser.ParseParts(responseBody, responseBoundary);
        var failures = parts.Where(p => p.StatusCode is < 200 or >= 300).ToList();
        if (failures.Count > 0)
        {
            // A changeset is all-or-nothing: if anything below failed,
            // nothing in this transaction was kept, whether or not this
            // particular failure was the one Dataverse chose to report (it
            // stops at the first failure and rolls the rest back rather than
            // continuing — no `Prefer: odata.continue-on-error` is sent).
            var details = string.Join(" | ", failures.Select(f => $"[{f.StatusCode}] {f.Body}"));
            throw new HttpRequestException($"Dataverse transaction rolled back — nothing in this batch of {writes.Count} write(s) was applied. {details}");
        }
    }

    /// <summary>
    /// Maps one <see cref="DataverseWrite"/> to the request
    /// <see cref="SendWriteAsync"/>/<see cref="ExecuteTransactionAsync"/>
    /// both need to build it — the method, the path relative to
    /// <c>api/data/v9.2/</c>, the JSON body, and (for an update, so a
    /// changed <c>DisplayName</c> doesn't wipe out other languages' labels)
    /// the <c>MSCRM.MergeLabels</c> header <see cref="UpdateEntityAsync"/>/
    /// <see cref="UpdateAttributeAsync"/> already sent before this existed.
    /// The one and only place these URLs are built for every case here —
    /// every individual write method above is now a one-line call into this
    /// (via <see cref="SendWriteAsync"/>), so there's nothing left to drift
    /// out of sync with what a batched write actually sends.
    /// </summary>
    private static (HttpMethod Method, string RelativeUrl, JsonObject Body, string? HeaderName, string? HeaderValue) ToHttpRequest(DataverseWrite write) => write switch
    {
        DataverseWrite.UpdateEntity w => (HttpMethod.Put, $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(w.EntityLogicalName)}')", w.Metadata, "MSCRM.MergeLabels", "true"),
        DataverseWrite.CreateAttribute w => (HttpMethod.Post, $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(w.EntityLogicalName)}')/Attributes", w.Metadata, null, null),
        DataverseWrite.UpdateAttribute w => (HttpMethod.Put, $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(w.EntityLogicalName)}')/Attributes(LogicalName='{Uri.EscapeDataString(w.AttributeLogicalName)}')", w.Metadata, "MSCRM.MergeLabels", "true"),
        DataverseWrite.CreateOneToManyRelationship w => (HttpMethod.Post, "RelationshipDefinitions", w.Metadata, null, null),
        DataverseWrite.CreateCustomerRelationships w => (HttpMethod.Post, "CreateCustomerRelationships", w.Body, null, null),
        DataverseWrite.InsertOptionValue w => (HttpMethod.Post, "InsertOptionValue", w.Body, null, null),
        DataverseWrite.UpdateOptionValue w => (HttpMethod.Post, "UpdateOptionValue", w.Body, null, null),
        DataverseWrite.OrderOptions w => (HttpMethod.Post, "OrderOption", w.Body, null, null),
        DataverseWrite.InsertStatusValue w => (HttpMethod.Post, "InsertStatusValue", w.Body, null, null),
        DataverseWrite.UpdateStateValue w => (HttpMethod.Post, "UpdateStateValue", w.Body, null, null),
        _ => throw new NotSupportedException($"Unhandled {nameof(DataverseWrite)} case: {write.GetType().Name}"),
    };

    /// <summary>
    /// Builds the raw <c>multipart/mixed</c> request text for
    /// <see cref="ExecuteTransactionAsync"/> — one outer <c>batch_*</c> part
    /// wrapping a single <c>changeset_*</c> part, itself wrapping one raw
    /// HTTP request per write, in order. Hand-built rather than composed via
    /// <c>System.Net.Http.MultipartContent</c>: the OData batch spec
    /// requires exact CRLF line endings throughout (Microsoft's own docs
    /// call this out explicitly — anything else risks a deserialization
    /// error), which is far easier to guarantee by writing the text directly
    /// than by trusting a general-purpose MIME multipart writer never
    /// deviates from it.
    /// </summary>
    private static string BuildTransactionRequestBody(string batchBoundary, string changesetBoundary, IReadOnlyList<DataverseWrite> writes)
    {
        var sb = new StringBuilder();

        void Line(string text = "") => sb.Append(text).Append("\r\n");

        Line($"--{batchBoundary}");
        Line($"Content-Type: multipart/mixed; boundary=\"{changesetBoundary}\"");
        Line();

        for (var i = 0; i < writes.Count; i++)
        {
            var (method, relativeUrl, body, headerName, headerValue) = ToHttpRequest(writes[i]);

            Line($"--{changesetBoundary}");
            Line("Content-Type: application/http");
            Line("Content-Transfer-Encoding: binary");
            Line($"Content-ID: {i + 1}");
            Line();
            Line($"{method.Method} /api/data/v9.2/{relativeUrl} HTTP/1.1");
            Line("Content-Type: application/json");
            if (headerName is not null)
            {
                Line($"{headerName}: {headerValue}");
            }

            Line();
            Line(body.ToJsonString());
        }

        Line($"--{changesetBoundary}--");
        Line();
        Line($"--{batchBoundary}--");

        return sb.ToString();
    }

    public async Task<string?> TryGetGlobalOptionSetJsonAsync(Uri environmentUrl, string accessToken, string name, CancellationToken cancellationToken)
    {
        // GlobalOptionSetDefinitions is typed as the abstract
        // OptionSetMetadataBase by default -- Options only exists on the
        // derived OptionSetMetadata type, so this needs the same type-cast
        // URL segment GetAttributeOptionSetJsonAsync uses for an attribute's
        // own OptionSet -- confirmed live (400: "Could not find a property
        // named 'Options' on type 'Microsoft.Dynamics.CRM.OptionSetMetadataBase'"
        // without it). Also confirmed live that, even after the type-cast,
        // $expand=Options itself still 400s ("not a navigation property or
        // complex property") -- unlike an attribute's own OptionSet/
        // GlobalOptionSet, Options has to be named in $select instead.
        var relativePath = $"GlobalOptionSetDefinitions(Name='{Uri.EscapeDataString(name)}')/Microsoft.Dynamics.CRM.OptionSetMetadata?$select=MetadataId,Name,DisplayName,Description,Options";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetGlobalOptionSetsJsonAsync(Uri environmentUrl, string accessToken, CancellationToken cancellationToken)
    {
        var relativePath = "GlobalOptionSetDefinitions/Microsoft.Dynamics.CRM.OptionSetMetadata?$select=MetadataId,Name,DisplayName,Description,Options";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>?> TryGetSolutionOptionSetMetadataIdsAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken)
    {
        var solutionId = await TryGetSolutionIdAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken);
        if (solutionId is null)
        {
            return null;
        }

        // componenttype 9 = Option Set. Confirmed live against this
        // environment's own componenttype global choice (its Options list
        // names value 9 "Option Set"), same empirical standard already
        // applied to Attribute (2)/View (26)/System Form (60) above.
        const int optionSetComponentType = 9;
        return await GetSolutionComponentObjectIdsAsync(environmentUrl, accessToken, solutionId.Value, optionSetComponentType, cancellationToken);
    }

    public async Task CreateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(environmentUrl, "GlobalOptionSetDefinitions", accessToken, HttpMethod.Post);
        request.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task UpdateGlobalOptionSetAsync(Uri environmentUrl, string accessToken, Guid metadataId, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(environmentUrl, $"GlobalOptionSetDefinitions({metadataId})", accessToken, HttpMethod.Put);
        request.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static string EscapeODataStringLiteral(string value) => value.Replace("'", "''");

    /// <summary>Shared by every "which components of type X does this solution contain" lookup.</summary>
    private async Task<IReadOnlySet<Guid>> GetSolutionComponentObjectIdsAsync(Uri environmentUrl, string accessToken, Guid solutionId, int componentType, CancellationToken cancellationToken)
    {
        var relativePath = $"solutioncomponents?$filter=_solutionid_value eq {solutionId} and componenttype eq {componentType}&$select=objectid";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(e => e.GetProperty("objectid").GetGuid())
            .ToHashSet();
    }

    private async Task<Guid?> TryGetSolutionIdAsync(Uri environmentUrl, string accessToken, string solutionUniqueName, CancellationToken cancellationToken)
    {
        var relativePath = $"solutions?$select=solutionid&$filter=uniquename eq '{Uri.EscapeDataString(solutionUniqueName)}'";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("value").EnumerateArray();
        return results.Any() ? results.First().GetProperty("solutionid").GetGuid() : null;
    }

    private static HttpRequestMessage CreateRequest(Uri environmentUrl, string relativePath, string accessToken, HttpMethod? method = null)
    {
        var baseUri = new Uri(environmentUrl, "/api/data/v9.2/");
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Dataverse request failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
    }

    private sealed record WhoAmIResponse(
        [property: JsonPropertyName("UserId")] Guid UserId,
        [property: JsonPropertyName("BusinessUnitId")] Guid BusinessUnitId,
        [property: JsonPropertyName("OrganizationId")] Guid OrganizationId);

    private sealed record SystemUserResponse([property: JsonPropertyName("fullname")] string? FullName);
}

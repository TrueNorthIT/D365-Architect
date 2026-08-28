using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace D365Architect.Services.Dataverse;

public sealed class DataverseClient(HttpClient httpClient) : IDataverseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        request.Content = JsonContent.Create(new { formxml = formXml });

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
        request.Content = JsonContent.Create(new { ParameterXml = parameterXml });

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

    public async Task UpdateEntityAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject entityMetadata, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(environmentUrl, $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')", accessToken, HttpMethod.Put);
        request.Headers.Add("MSCRM.MergeLabels", "true");
        request.Content = JsonContent.Create(entityMetadata);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

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

    public async Task UpdateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string attributeLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken)
    {
        // No type-cast segment here, unlike the GET -- confirmed against
        // Microsoft's own documented example: the type comes from the body's
        // own "@odata.type", not the URL.
        var relativePath = $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')" +
            $"/Attributes(LogicalName='{Uri.EscapeDataString(attributeLogicalName)}')";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken, HttpMethod.Put);
        request.Headers.Add("MSCRM.MergeLabels", "true");
        request.Content = JsonContent.Create(attributeMetadata);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task CreateAttributeAsync(Uri environmentUrl, string accessToken, string entityLogicalName, JsonObject attributeMetadata, CancellationToken cancellationToken)
    {
        var relativePath = $"EntityDefinitions(LogicalName='{Uri.EscapeDataString(entityLogicalName)}')/Attributes";

        using var request = CreateRequest(environmentUrl, relativePath, accessToken, HttpMethod.Post);
        request.Content = JsonContent.Create(attributeMetadata);

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

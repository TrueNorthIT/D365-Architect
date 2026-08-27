using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        var relativePath = $"solutioncomponents?$filter=_solutionid_value eq {solutionId} and componenttype eq {attributeComponentType}&$select=objectid";

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

    private static HttpRequestMessage CreateRequest(Uri environmentUrl, string relativePath, string accessToken)
    {
        var baseUri = new Uri(environmentUrl, "/api/data/v9.2/");
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativePath));
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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Declarative_D365.Services.Dataverse;

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

using System.Text;
using System.Text.Json;

namespace D365Architect.Services.Authentication;

/// <summary>
/// Works out a readable "who is this" from an access token's own claims, for
/// a non-interactive sign-in where there's no MSAL account to ask — a
/// service principal or managed identity has no username at all.
///
/// Only decodes the JWT payload; it never validates the signature. That's
/// fine here because the result is only ever displayed, never trusted:
/// Dataverse itself is what validates the token.
/// </summary>
internal static class AccessTokenIdentity
{
    /// <summary>
    /// Returns the signed-in user's name (<c>upn</c>, <c>unique_name</c> or
    /// <c>preferred_username</c>) when there is one, otherwise the
    /// application's ID (<c>appid</c>/<c>azp</c>) — with its
    /// <c>app_displayname</c> in front when the token carries it. Null when
    /// the token can't be decoded or carries none of those claims.
    /// </summary>
    public static string? Describe(string accessToken)
    {
        var claims = TryReadPayload(accessToken);
        if (claims is null)
        {
            return null;
        }

        var user = Claim(claims.Value, "upn") ?? Claim(claims.Value, "unique_name") ?? Claim(claims.Value, "preferred_username");
        if (user is not null)
        {
            return user;
        }

        var appId = Claim(claims.Value, "appid") ?? Claim(claims.Value, "azp");
        if (appId is null)
        {
            return null;
        }

        var appName = Claim(claims.Value, "app_displayname");
        return appName is null ? $"application {appId}" : $"{appName} (application {appId})";
    }

    private static JsonElement? TryReadPayload(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        // The payload is base64url without padding: swap the two URL-safe
        // characters back and re-pad before handing it to the ordinary decoder.
        var base64 = parts[1].Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Claim(JsonElement claims, string name) =>
        claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}

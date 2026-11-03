using System.Text;
using System.Text.Json;
using D365Architect.Services.Authentication;
using Xunit;

namespace D365Architect.Tests.Services.Authentication;

/// <summary>
/// Covers <see cref="AccessTokenIdentity.Describe"/>, which picks the
/// identity `whoami` shows for a non-interactive sign-in out of the access
/// token's own claims. Tokens here are unsigned fakes: only the payload is
/// ever read.
/// </summary>
public sealed class AccessTokenIdentityTests
{
    private const string AppId = "22222222-2222-2222-2222-222222222222";

    private static string Token(object claims)
    {
        static string Base64Url(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Base64Url("{\"alg\":\"none\"}")}.{Base64Url(JsonSerializer.Serialize(claims))}.signature";
    }

    [Fact]
    public void Describe_UserToken_ReturnsUpn()
    {
        var token = Token(new { upn = "jane@contoso.com", unique_name = "other@contoso.com", appid = AppId });

        Assert.Equal("jane@contoso.com", AccessTokenIdentity.Describe(token));
    }

    [Fact]
    public void Describe_UserTokenWithoutUpn_FallsBackToUniqueNameThenPreferredUsername()
    {
        Assert.Equal("guest@contoso.com", AccessTokenIdentity.Describe(Token(new { unique_name = "guest@contoso.com", appid = AppId })));
        Assert.Equal("v2@contoso.com", AccessTokenIdentity.Describe(Token(new { preferred_username = "v2@contoso.com", azp = AppId })));
    }

    [Fact]
    public void Describe_ServicePrincipalTokenWithDisplayName_ReturnsNameAndAppId()
    {
        var token = Token(new { appid = AppId, app_displayname = "Pipeline Deployer" });

        Assert.Equal($"Pipeline Deployer (application {AppId})", AccessTokenIdentity.Describe(token));
    }

    [Fact]
    public void Describe_ServicePrincipalTokenWithoutDisplayName_ReturnsAppId()
    {
        Assert.Equal($"application {AppId}", AccessTokenIdentity.Describe(Token(new { appid = AppId })));
    }

    [Fact]
    public void Describe_V2ServicePrincipalToken_UsesAzp()
    {
        Assert.Equal($"application {AppId}", AccessTokenIdentity.Describe(Token(new { azp = AppId })));
    }

    [Fact]
    public void Describe_BlankUserClaims_AreSkipped()
    {
        Assert.Equal($"application {AppId}", AccessTokenIdentity.Describe(Token(new { upn = "", unique_name = "  ", appid = AppId })));
    }

    // Payload lengths of 1, 2 and 3 bytes past a multiple of 3 exercise
    // every base64url padding case.
    [Theory]
    [InlineData("a@b.c")]
    [InlineData("ab@b.c")]
    [InlineData("abc@b.c")]
    public void Describe_PayloadNeedingAnyAmountOfPadding_Decodes(string upn)
    {
        Assert.Equal(upn, AccessTokenIdentity.Describe(Token(new { upn })));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("header.!!!not-base64!!!.signature")]
    [InlineData("header.bm90IGpzb24.signature")] // "not json"
    [InlineData("header.WzEsMl0.signature")] // [1,2], valid JSON but not an object
    public void Describe_UndecodableToken_ReturnsNull(string token)
    {
        Assert.Null(AccessTokenIdentity.Describe(token));
    }

    [Fact]
    public void Describe_TokenWithNoIdentityClaims_ReturnsNull()
    {
        Assert.Null(AccessTokenIdentity.Describe(Token(new { tid = "11111111-1111-1111-1111-111111111111" })));
    }
}

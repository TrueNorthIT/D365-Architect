using D365Architect.Services.Authentication;
using Microsoft.Identity.Client;
using Xunit;

namespace D365Architect.Tests.Services.Authentication;

/// <summary>
/// Pins the silent-token path's exception wrapping (the part of
/// <c>MsalAuthenticationService.GetCurrentContextAsync</c> a live MSAL
/// application is too heavy to drive in a unit test — the wrapping is
/// factored out so it's testable directly). The silent path catches the
/// broader <see cref="MsalException"/>, mirroring the interactive path:
/// only the token-expiry case gets the "expired" message, every other
/// failure (network, broker/CA, config) surfaces as an
/// <see cref="AuthenticationRequiredException"/> that keeps the original
/// exception as its inner.
/// </summary>
public sealed class MsalAuthenticationServiceSilentFailureTests
{
    // An expired token is the one MSAL failure with its own dedicated message — "sign in again" — not a generic error.
    [Fact]
    public void UiRequiredIsWrappedAsAnExpiredSignIn()
    {
        var wrapped = MsalAuthenticationService.WrapSilentFailure(new MsalUiRequiredException("AADSTS700016", "token expired"));

        Assert.IsType<AuthenticationRequiredException>(wrapped);
        Assert.Contains("expired", wrapped.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(wrapped.InnerException);
    }

    // Any other MSAL failure keeps its cause: the message still carries MSAL's own message, and the original
    // exception is the inner one — so the user sees an actionable authentication error instead of a crash.
    [Fact]
    public void OtherMsalFailuresAreWrappedWithTheirCauseKept()
    {
        var inner = new MsalException("some-error-code", "AADSTS7000215: user account disabled");
        var wrapped = MsalAuthenticationService.WrapSilentFailure(inner);

        var auth = Assert.IsType<AuthenticationRequiredException>(wrapped);
        Assert.Contains("AADSTS7000215", auth.Message, StringComparison.Ordinal);
        Assert.Same(inner, auth.InnerException);
    }
}

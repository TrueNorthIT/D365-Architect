namespace Declarative_D365.Services.Authentication;

/// <summary>
/// Thrown whenever a command needs a signed-in session and there isn't one
/// (never signed in, or the sign-in has expired). Commands catch this and
/// show <see cref="Exception.Message"/> as a plain, user-facing error
/// rather than a stack trace.
/// </summary>
public sealed class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException(string message)
        : base(message)
    {
    }

    public AuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

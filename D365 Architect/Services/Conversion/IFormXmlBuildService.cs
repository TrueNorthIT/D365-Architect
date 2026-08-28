using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Rebuilds FormXML for a single curated <see cref="FormDefinition"/> — the
/// live-environment counterpart to calling <see cref="FormXmlWriter.Write"/>
/// directly. Looks up the form's current, live <c>formxml</c> first (by
/// table + display name, the only identity a `*.form.yml` file carries) so
/// the rebuild patches onto that document instead of building one from
/// scratch — see <see cref="FormXmlWriter"/>'s own doc comment for exactly
/// what that changes.
/// </summary>
public interface IFormXmlBuildService
{
    /// <param name="environmentUrl">The D365 environment to look the form up in.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="form">The curated form to render as FormXML.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="NotSupportedException"><paramref name="form"/> is a dashboard.</exception>
    /// <exception cref="Dataverse.AmbiguousSystemFormException">More than one live form matches <paramref name="form"/>'s table + name.</exception>
    Task<string> BuildFormXmlAsync(Uri environmentUrl, string accessToken, FormDefinition form, CancellationToken cancellationToken);
}

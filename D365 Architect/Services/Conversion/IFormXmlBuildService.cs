using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Rebuilds FormXML for a single curated <see cref="FormDefinition"/> — the
/// live-environment counterpart to calling <see cref="FormXmlWriter.Write"/>
/// directly. Looks up the form's current, live <c>formxml</c> first — by
/// the YAML's own <c>FormId</c> when it has one (the ordinary case, and
/// the only way to disambiguate several forms sharing a display name; see
/// <see cref="FormDefinition.FormId"/>'s own doc comment), falling back to
/// table + display name for a file exported before that field existed —
/// so the rebuild patches onto that document instead of building one from
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
    /// <exception cref="Dataverse.AmbiguousSystemFormException"><paramref name="form"/> has no <c>FormId</c> and more than one live form matches its table + name.</exception>
    Task<FormXmlBuildResult> BuildFormXmlAsync(Uri environmentUrl, string accessToken, FormDefinition form, CancellationToken cancellationToken);
}

/// <param name="FormXml">The rebuilt FormXML.</param>
/// <param name="IdentityMismatchWarning">
/// Set when the YAML's own <c>FormId</c> resolved to a live form whose
/// table and/or name no longer match the YAML's own <c>Entity</c>/<c>Name</c>
/// — see <see cref="Dataverse.ExistingSystemForm.BuildIdentityMismatchWarning"/>.
/// Never a reason to stop (the id is still authoritative), just worth a
/// human's attention. Null when the lookup was by table + name instead, or
/// when it matched.
/// </param>
public sealed record FormXmlBuildResult(string FormXml, string? IdentityMismatchWarning);

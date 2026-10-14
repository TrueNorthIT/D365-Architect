using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class FormXmlBuildService(IDataverseClient dataverseClient) : IFormXmlBuildService
{
    public async Task<FormXmlBuildResult> BuildFormXmlAsync(Uri environmentUrl, string accessToken, FormDefinition form, CancellationToken cancellationToken)
    {
        string? existingFormXml;
        string? identityMismatchWarning = null;

        if (form.FormId is { } formId)
        {
            // Preferred whenever the YAML has one — an id can't go
            // ambiguous the way table + name can (several forms sharing a
            // display name; see AmbiguousSystemFormException). A FormId
            // that no longer resolves to anything (the form was deleted)
            // is treated the same as "doesn't exist yet" below, not as an
            // error — this command has always fallen back to building
            // fresh from the YAML rather than refusing, and a stale id is
            // just another way to reach that same case.
            var existing = await dataverseClient.TryGetSystemFormByIdAsync(environmentUrl, accessToken, formId, cancellationToken);
            existingFormXml = existing?.FormXml;
            identityMismatchWarning = existing?.BuildIdentityMismatchWarning(form);
        }
        else
        {
            // Fallback for a *.form.yml exported before FormId existed.
            existingFormXml = await dataverseClient.TryGetSystemFormXmlAsync(environmentUrl, accessToken, form.Entity, form.Name, cancellationToken);
        }

        var existingForm = existingFormXml is not null ? XElement.Parse(existingFormXml) : null;
        var formXml = FormXmlWriter.Write(form, existingForm);

        return new FormXmlBuildResult(formXml, identityMismatchWarning);
    }
}

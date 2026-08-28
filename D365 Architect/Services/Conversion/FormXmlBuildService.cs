using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class FormXmlBuildService(IDataverseClient dataverseClient) : IFormXmlBuildService
{
    public async Task<string> BuildFormXmlAsync(Uri environmentUrl, string accessToken, FormDefinition form, CancellationToken cancellationToken)
    {
        var existingFormXml = await dataverseClient.TryGetSystemFormXmlAsync(environmentUrl, accessToken, form.Entity, form.Name, cancellationToken);
        var existingForm = existingFormXml is not null ? XElement.Parse(existingFormXml) : null;

        return FormXmlWriter.Write(form, existingForm);
    }
}

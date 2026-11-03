using System.Text.Json;
using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class FormExportService(IDataverseClient dataverseClient, FormJsonDefinitionReader reader) : IFormExportService
{
    public async Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken)
    {
        var allowedFormIds = await ResolveAllowedFormIdsAsync(environmentUrl, accessToken, entityLogicalName, solutionUniqueName, formId, cancellationToken);

        var json = await dataverseClient.GetFormDefinitionsJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        var forms = reader.Read(json, allowedFormIds);

        var usedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return forms
            .Select(form => new ExportedForm(AssetFileNaming.MakeUnique(BuildStem(form), usedStems), FormYamlSerializer.ToYaml(form)))
            .ToList();
    }

    public async Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        var allowedFormIds = await ResolveAllowedFormIdsAsync(environmentUrl, accessToken, entityLogicalName, solutionUniqueName, formId: null, cancellationToken);

        var json = await dataverseClient.GetFormSummariesJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        return reader.ReadSummaries(json, allowedFormIds);
    }

    /// <summary>
    /// Unlike a view, two forms on the same table can easily share a
    /// display name — confirmed live (a real table with three forms all
    /// named "Information", one of each of three different types). A bare
    /// <see cref="AssetFileNaming.Slugify"/> of the name alone would only
    /// tell those apart with an arbitrary "-2"/"-3" suffix from
    /// <see cref="AssetFileNaming.MakeUnique"/> — technically unique, but
    /// meaningless to read back. Folding the form's own type in as its own
    /// dot-separated segment — the same convention this tool's own
    /// `.form.yml` suffix already uses — keeps the first part of the
    /// filename as just the plain friendly name, with the type read as a
    /// second, clearly-separate component rather than fused into one
    /// hyphenated blob: "Information" (Quick View Form) becomes
    /// "information.quick-view-form.form.yml", not
    /// "information-quick-view-form.form.yml" (ambiguous where the name
    /// ends and the type begins). The common case (an ordinary Main
    /// form — the only type <see cref="FormDefinition.Type"/> is left null
    /// for, same convention as everywhere else this tool omits a
    /// common-case value) stays exactly as before, with no type segment at
    /// all. <see cref="AssetFileNaming.MakeUnique"/> still runs afterward as
    /// the final safety net, for the rarer case of two forms sharing both a
    /// name and a type.
    /// </summary>
    public ExportedForm ExportFormFromXml(string path, string entityLogicalName, string? type)
    {
        var typeCode = FormJsonDefinitionReader.TryGetFormTypeCode(type)
            ?? throw new InvalidDataException($"'{type}' isn't a form type. Use one of the systemform type labels, e.g. Main, Quick View Form or Card.");

        var local = FormXmlFile.LoadLocalForm(path);

        // Wrapped in the same shape GetFormDefinitionsJsonAsync returns, so the
        // file goes through exactly the reader a live export does and yields
        // the same YAML for the same FormXML. A bare form has no name of its
        // own; the file name stands in for it.
        var json = JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new
                {
                    formid = local.FormId,
                    name = local.Name ?? Path.GetFileNameWithoutExtension(path),
                    objecttypecode = entityLogicalName,
                    type = typeCode,
                    formxml = local.Form.ToString(SaveOptions.DisableFormatting),
                },
            },
        });

        var form = reader.Read(json).Single();
        return new ExportedForm(BuildStem(form), FormYamlSerializer.ToYaml(form));
    }

    private static string BuildStem(FormDefinition form) =>
        form.Type is null
            ? AssetFileNaming.Slugify(form.Name)
            : $"{AssetFileNaming.Slugify(form.Name)}.{AssetFileNaming.Slugify(form.Type)}";

    private async Task<IReadOnlySet<Guid>?> ResolveAllowedFormIdsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedFormIds = null;
        if (solutionUniqueName is not null)
        {
            allowedFormIds = await dataverseClient.TryGetSolutionSystemFormIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);

            // Same "the table's own Include-Subcomponents membership means
            // no separate SystemForm solutioncomponents exist at all" gap
            // as TableExportService/ViewExportService — see
            // IDataverseClient.IsSolutionEntityIncludingSubcomponentsAsync's
            // own doc comment.
            if (await dataverseClient.IsSolutionEntityIncludingSubcomponentsAsync(environmentUrl, accessToken, solutionUniqueName, entityLogicalName, cancellationToken))
            {
                allowedFormIds = null;
            }
        }

        if (formId is null)
        {
            return allowedFormIds;
        }

        // A form id outside the solution's own forms (when both are given)
        // should export nothing, not silently fall back to the solution's
        // full set — this narrows to exactly the one id either way.
        return allowedFormIds is null || allowedFormIds.Contains(formId.Value)
            ? new HashSet<Guid> { formId.Value }
            : new HashSet<Guid>();
    }
}

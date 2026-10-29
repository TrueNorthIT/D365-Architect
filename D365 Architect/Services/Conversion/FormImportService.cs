using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class FormImportService(IDataverseClient dataverseClient, FormJsonDefinitionReader formJsonDefinitionReader) : IFormImportService
{
    public async Task<FormImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, FormDefinition form, CancellationToken cancellationToken)
    {
        var formType = FormJsonDefinitionReader.TryGetFormTypeCode(form.Type)
            ?? throw new InvalidDataException($"Form '{form.Name}' has type '{form.Type}', which isn't a Dataverse form type. Use one of the labels 'form export' writes (e.g. 'Main', 'Quick View Form', 'Quick Create', 'Card'), or leave 'type' out for a main form.");

        // A formId is authoritative and never falls back to a name match or a
        // create (see FormNotFoundException). Without one, name + type is the
        // identity (REQ-29): a fresh custom table's main, card and quick view
        // forms are all named "Information" (confirmed live), so name alone
        // can't tell them apart. No match is a create; several is refused,
        // listing the ids to pick from (REQ-31).
        ExistingSystemForm? existing;
        if (form.FormId is { } formId)
        {
            existing = await dataverseClient.TryGetSystemFormByIdAsync(environmentUrl, accessToken, formId, cancellationToken)
                ?? throw new FormNotFoundException(form.Entity, form.Name, formId);
        }
        else
        {
            var matches = await dataverseClient.FindSystemFormsAsync(environmentUrl, accessToken, form.Entity, form.Name, formType, cancellationToken);
            existing = matches.Count switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new AmbiguousSystemFormException(form.Entity, form.Name, matches.Select(m => m.FormId).ToList()),
            };
        }

        if (existing is null)
        {
            return PreviewCreate(form, formType);
        }

        var identityMismatchWarning = existing.BuildIdentityMismatchWarning(form);

        var existingRoot = XElement.Parse(existing.FormXml);
        var newFormXml = FormXmlWriter.Write(form, existingRoot);

        // FormControlValidator first: it needs the curated FormDefinition
        // and the existing raw document directly (to tell a control that's
        // always been classid-less from one newly losing or never getting
        // one), neither of which survives into newFormXml alone. Its
        // findings are FormXmlValidationMessage the same as FormXmlValidator's
        // own — see that type's own doc comment — so both flow through the
        // identical IsKnownHarmless-gated blocking `form import` already
        // applies, with no separate plumbing needed.
        //
        // FormControlValidator always runs, even on a no-op: it checks the
        // local YAML's own control definitions (e.g. both 'control' and
        // 'customControlId' set), which can be broken without changing the
        // rendered FormXML at all. Only FormXmlValidator (the FormXML schema
        // check) is skipped when the rebuild matches live: there is nothing
        // to write, so validating would only block a no-op import over
        // content Dataverse already holds.
        var existingForm = DecomposeExisting(form, existing.FormXml);
        var existingComparableFormXml = FormXmlWriter.Write(existingForm, existingRoot);

        // Rebuilt through the exact same writer, base document, and
        // deterministic id rules as newFormXml — see
        // FormImportPreview.ExistingComparableFormXml's own doc comment for
        // why comparing this canonicalized rebuild, rather than
        // existing.FormXml itself, is what makes the diff meaningful.
        IReadOnlyList<FormXmlValidationMessage> violations = existingComparableFormXml == newFormXml
            ? FormControlValidator.Validate(form, existingRoot).ToList()
            : FormControlValidator.Validate(form, existingRoot)
                .Concat(FormXmlValidator.Validate(newFormXml))
                .ToList();

        var entity = existing.EntityLogicalName ?? form.Entity;

        return new FormImportPreview(existing.FormId, entity, existing.FormXml, newFormXml, existingComparableFormXml, violations, identityMismatchWarning)
        {
            Name = form.Name,
            FormType = formType,
        };
    }

    /// <summary>
    /// A form that matches nothing: built from scratch (no live document to
    /// patch onto), validated in full, and compared against nothing, so the
    /// whole document is the change.
    /// </summary>
    private static FormImportPreview PreviewCreate(FormDefinition form, int formType)
    {
        var newFormXml = FormXmlWriter.Write(form);
        var violations = FormControlValidator.Validate(form, new XElement("form"))
            .Concat(FormXmlValidator.Validate(newFormXml))
            .ToList();

        return new FormImportPreview(Guid.Empty, form.Entity, "", newFormXml, "", violations)
        {
            IsCreate = true,
            Name = form.Name,
            FormType = formType,
            Description = form.Description,
        };
    }

    public Task ApplyAsync(Uri environmentUrl, string accessToken, FormImportPreview preview, CancellationToken cancellationToken)
        => ApplyAsync(environmentUrl, accessToken, preview, solutionUniqueName: null, cancellationToken);

    public Task ApplyAsync(Uri environmentUrl, string accessToken, FormImportPreview preview, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        if (!preview.IsCreate)
        {
            return dataverseClient.UpdateSystemFormXmlAsync(environmentUrl, accessToken, preview.FormId, preview.NewFormXml, solutionUniqueName, cancellationToken);
        }

        // The writable SystemForm columns Learn lists (no worked create
        // example exists there, so the minimum accepted set is unconfirmed
        // until run live): objecttypecode is the table's logical name, type
        // the option-set integer.
        var body = new JsonObject
        {
            ["name"] = preview.Name,
            ["objecttypecode"] = preview.Entity,
            ["type"] = preview.FormType,
            ["formxml"] = preview.NewFormXml,
        };

        if (preview.Description is not null)
        {
            body["description"] = preview.Description;
        }

        return dataverseClient.CreateSystemFormAsync(environmentUrl, accessToken, body, solutionUniqueName, cancellationToken);
    }

    public Task PublishAsync(Uri environmentUrl, string accessToken, FormImportPreview preview, CancellationToken cancellationToken)
        => dataverseClient.PublishEntityAsync(environmentUrl, accessToken, preview.Entity, cancellationToken);

    /// <summary>
    /// Decomposes the form's current live FormXML the same way `form
    /// export` itself would, so it can be rebuilt back through
    /// <see cref="FormXmlWriter"/> for a fair comparison — see
    /// <see cref="FormImportPreview.ExistingComparableFormXml"/>. Only
    /// <paramref name="form"/>'s own <c>Name</c>/<c>Entity</c> are needed
    /// alongside the live <paramref name="existingFormXml"/> to decompose
    /// it; its other, record-level fields (<c>Description</c>, <c>Type</c>,
    /// ...) never enter into this at all, since none of them live inside
    /// <c>formxml</c> in the first place.
    /// </summary>
    private FormDefinition DecomposeExisting(FormDefinition form, string existingFormXml)
    {
        var wrapped = JsonSerializer.Serialize(new
        {
            value = new[] { new { name = form.Name, objecttypecode = form.Entity, formxml = existingFormXml } },
        });

        return formJsonDefinitionReader.Read(wrapped)[0];
    }
}

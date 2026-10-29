namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when <c>form import</c> is given a `*.form.yml` file whose
/// <c>formId</c> (see <see cref="Conversion.Models.FormDefinition.FormId"/>)
/// resolves to no live form. A stale id is an error, never a fallback to the
/// name or a create: it most likely means the form was deleted, and silently
/// creating a second one would hide that. A file with no <c>formId</c> that
/// matches no form by table + name + type is created instead, so it
/// never gets here.
/// </summary>
public sealed class FormNotFoundException(string entityLogicalName, string formName, Guid formId)
    : Exception($"No form with id '{formId}' was found (this YAML's own record of '{formName}' on '{entityLogicalName}'). It may have been deleted since this file was last exported — re-export to get its current id, restore the deleted form, or remove 'formId' from the file to create it as a new form.")
{
    public string EntityLogicalName { get; } = entityLogicalName;
    public string FormName { get; } = formName;
    public Guid? FormId { get; } = formId;
}

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when <c>form import</c> can't find a live form matching a
/// `*.form.yml` file — by its <c>formid</c> when the file has one (see
/// <see cref="Conversion.Models.FormDefinition.FormId"/>), or by table +
/// name as a fallback for a file exported before that field existed.
/// Unlike <c>form build-xml</c> (which falls back to building fresh when
/// this happens, since it only ever writes a local file), import has
/// nothing to update without an existing record — creating a brand-new
/// form isn't supported yet.
/// </summary>
public sealed class FormNotFoundException : Exception
{
    public FormNotFoundException(string entityLogicalName, string formName)
        : base($"No form named '{formName}' was found on '{entityLogicalName}'. Creating a new form isn't supported yet — form import only updates one that already exists in Dataverse.")
    {
        EntityLogicalName = entityLogicalName;
        FormName = formName;
    }

    public FormNotFoundException(string entityLogicalName, string formName, Guid formId)
        : base($"No form with id '{formId}' was found (this YAML's own record of '{formName}' on '{entityLogicalName}'). It may have been deleted since this file was last exported — re-export to get its current id, or restore the deleted form first.")
    {
        EntityLogicalName = entityLogicalName;
        FormName = formName;
        FormId = formId;
    }

    public string EntityLogicalName { get; }
    public string FormName { get; }
    public Guid? FormId { get; }
}

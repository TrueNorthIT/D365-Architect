namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown when <c>form import</c> can't find a live form matching a
/// `*.form.yml` file's table + name (the only identity it carries — see
/// <see cref="Conversion.Models.FormDefinition"/>'s own doc comment).
/// Unlike <c>form build-xml</c> (which falls back to building fresh when
/// this happens, since it only ever writes a local file), import has
/// nothing to update without an existing record — creating a brand-new
/// form isn't supported yet.
/// </summary>
public sealed class FormNotFoundException(string entityLogicalName, string formName)
    : Exception($"No form named '{formName}' was found on '{entityLogicalName}'. Creating a new form isn't supported yet — form import only updates one that already exists in Dataverse.")
{
    public string EntityLogicalName { get; } = entityLogicalName;
    public string FormName { get; } = formName;
}

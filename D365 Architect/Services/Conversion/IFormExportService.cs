namespace D365Architect.Services.Conversion;

/// <summary>
/// Exports every form live from Dataverse for a single table and converts
/// each into this tool's curated YAML — the <see cref="IViewExportService"/>
/// counterpart for forms. A table can carry many forms, so this returns one
/// exported result per form rather than a single YAML string.
/// </summary>
public interface IFormExportService
{
    /// <param name="environmentUrl">The D365 environment to read from.</param>
    /// <param name="accessToken">A bearer token already issued for <paramref name="environmentUrl"/>.</param>
    /// <param name="entityLogicalName">Logical name of the table whose forms to export, e.g. "account".</param>
    /// <param name="solutionUniqueName">
    /// When given, scopes the export to just the forms that solution
    /// actually customizes (its System Form solution components), instead
    /// of every form defined on the table.
    /// </param>
    /// <param name="formId">
    /// When given, exports only this one form (its systemform id) instead
    /// of every form on the table — how `form export` behaves once a form
    /// has been chosen, whether via <c>--form-id</c> or the interactive
    /// picker (see <see cref="ListFormsAsync"/>). Combined with
    /// <paramref name="solutionUniqueName"/> when both are given: a form id
    /// outside that solution's forms exports nothing rather than falling
    /// back to it.
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="InvalidDataException">Dataverse returned metadata this tool doesn't understand yet.</exception>
    /// <exception cref="Dataverse.SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution in the environment.</exception>
    Task<IReadOnlyList<ExportedForm>> ExportFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, Guid? formId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists every form on a table cheaply (id, name, type — never their
    /// full FormXml) for `form export`'s interactive picker, when it's run
    /// without <c>--form-id</c>. Honors <paramref name="solutionUniqueName"/>
    /// the same way <see cref="ExportFormsAsync"/> does.
    /// </summary>
    /// <exception cref="InvalidDataException">Dataverse returned metadata this tool doesn't understand yet.</exception>
    /// <exception cref="Dataverse.SolutionNotFoundException"><paramref name="solutionUniqueName"/> doesn't match any solution in the environment.</exception>
    Task<IReadOnlyList<FormSummary>> ListFormsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken);
}

/// <summary>
/// One exported form: its curated YAML, plus the filesystem-safe stem to
/// write it under (e.g. "account-main-form" for "account-main-form.form.yml") —
/// derived from the form's display name, since that's a form's only
/// practical identity (see <see cref="Models.FormDefinition"/>).
/// </summary>
public sealed record ExportedForm(string FileNameStem, string Yaml);

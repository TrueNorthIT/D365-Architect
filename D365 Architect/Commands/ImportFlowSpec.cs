using D365Architect.Services.Authentication;

namespace D365Architect.Commands;

/// <summary>
/// The parts of the shared `import` flow (see <see cref="ImportRunner"/>)
/// that actually differ between `form import`, `table import`, and `view
/// import` — each command builds one of these with its own delegates in
/// its `ExecuteAsync` and hands it, alongside its own
/// <c>IImportService&lt;TInput,TPreview&gt;</c>, to the injected
/// <see cref="ImportRunner"/>, rather than the flow being pulled out into a
/// base class every command would have to inherit from.
///
/// <c>PreviewAsync</c> itself isn't here: every import service's preview
/// call has the exact same shape (see
/// <see cref="Services.Conversion.IImportService{TInput,TPreview}"/>), so
/// <see cref="ImportRunner"/> calls it directly on the service instance —
/// only the status message shown while it runs actually varies.
/// </summary>
/// <typeparam name="TSettings">The command's own `Settings`, extending <see cref="ImportSettingsBase"/>.</typeparam>
/// <typeparam name="TInput">The parsed local YAML (e.g. <c>FormDefinition</c>).</typeparam>
/// <typeparam name="TPreview">The result of previewing the change (e.g. <c>FormImportPreview</c>).</typeparam>
public sealed class ImportFlowSpec<TSettings, TInput, TPreview>
    where TSettings : ImportSettingsBase
    where TInput : class
{
    /// <summary>Reads and parses the command's <c>--input</c> file. Returns null (having already reported why, e.g. via a <c>YamlFileReader</c>) if it's missing/invalid — <see cref="ImportRunner"/> then exits 1 without going near Dataverse.</summary>
    public required Func<TSettings, CancellationToken, Task<TInput?>> ReadInputAsync { get; init; }

    /// <summary>The name shown in "Changes for '...':" and (via <see cref="PrintSuccess"/>) the success message — a form/table/view's own name.</summary>
    public required Func<TInput, string> SubjectName { get; init; }

    /// <summary>The <c>AnsiConsole.Status()</c> message shown while the service's <c>PreviewAsync</c> runs — the only part of previewing that actually differs per command (e.g. "rebuilding its FormXML").</summary>
    public required Func<TInput, string> PreviewStatusMessage { get; init; }

    /// <summary>Called right after the preview is built, before any of the checks below — form import uses this to print an identity-mismatch warning that's independent of whether anything actually changed. No-op by default.</summary>
    public Action<TPreview>? OnPreviewBuilt { get; init; }

    /// <summary>Non-null (appended after "No changes — ") to bail out — green line — before anything else is printed. Null means keep going.</summary>
    public required Func<TPreview, string?> SkipBeforePrinting { get; init; }

    /// <summary>Prints the diff/plan body for one preview, including any extra per-command detail (a table also prints its column plan here; a form also prints its schema violations here). Must own its own trailing blank line if it wants one — nothing is added around this call.</summary>
    public required Action<TPreview> PrintChanges { get; init; }

    /// <summary>Non-null (the complete markup line to print) to bail out after printing but before importing — e.g. table import's "nothing above is actually applicable" once a column shows up in the diff without being one this tool can act on. Never skips by default.</summary>
    public Func<TPreview, string?> SkipAfterPrinting { get; init; } = _ => null;

    /// <summary>Non-null (the reason, appended after "[red]Refusing to import.[/] ") to refuse the import outright — e.g. form import's schema-violation gate. Never blocks by default.</summary>
    public Func<TSettings, TPreview, string?> BlockReason { get; init; } = (_, _) => null;

    /// <summary>The <c>AnsiConsole.Status()</c> message shown while <see cref="ApplyAsync"/> runs (e.g. "Importing and publishing..."). Always static text in practice — unlike <see cref="PreviewStatusMessage"/>, nothing here needs the subject's name.</summary>
    public required string ApplyStatusMessage { get; init; }

    /// <summary>
    /// Writes the change to Dataverse — a raw call, not wrapped in
    /// <c>AnsiConsole.Status()</c> itself (<see cref="ImportRunner"/> does
    /// that, using <see cref="ApplyStatusMessage"/>). Usually just the
    /// service's own <c>ApplyAsync</c>, but not always: form import's
    /// separate publish step doesn't fit
    /// <see cref="Services.Conversion.IImportService{TInput,TPreview}"/>'s
    /// shared shape (table/view import never publish), so it makes that
    /// extra call here too, right after the service's own <c>ApplyAsync</c>.
    /// </summary>
    public required Func<AuthenticatedContext, TPreview, CancellationToken, Task> ApplyAsync { get; init; }

    /// <summary>Prints the success line(s) — table/view also print a "publish separately" note here that form doesn't need (it publishes itself).</summary>
    public required Action<TInput, TPreview> PrintSuccess { get; init; }

    /// <summary>Given the caught exception and the input, non-null (the complete markup line to print, own color/formatting included) if it's one of this command's own known/expected exceptions; null lets it propagate uncaught, same as before this was centralized.</summary>
    public required Func<Exception, TInput, string?> FormatDomainException { get; init; }
}

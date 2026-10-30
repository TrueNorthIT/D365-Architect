using System.ComponentModel;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Form;

/// <summary>
/// `d365architect form import --input account-main-form.form.yml [--solution UNIQUE_NAME] [--yes] [--whatif]`
/// Writes a `*.form.yml` file's rebuilt FormXML directly back into
/// Dataverse — straight from the YAML, not through `form build-xml` first.
/// That command exists for a human to inspect/validate the rebuilt FormXML
/// locally; it's never a required intermediary, and this command doesn't
/// call it or share any state with it — <see cref="IFormImportService"/>
/// does its own independent retrieve-and-patch. Needs sign-in.
///
/// Before writing anything: retrieves the form's current live FormXML,
/// rebuilds it (patched onto that live document, the same mechanism
/// `build-xml` uses — see <see cref="Services.Conversion.FormXmlWriter"/>),
/// validates it against Microsoft's own FormXML schema, and prints a
/// line-level diff of the actual FormXML on both sides — the live form
/// rebuilt through the same writer/id-rules as the new one, specifically so
/// the two are comparable rather than differing on every element purely
/// from resynthesized ids (see
/// <see cref="Services.Conversion.FormImportPreview.ExistingComparableFormXml"/>'s
/// own doc comment) — the concrete answer to "must have a way to check
/// differences between client and server". Nothing is written until you
/// confirm (or pass <c>--yes</c>), and if the two sides are identical,
/// nothing is written at all. Pass <c>--whatif</c> to see just the diff and
/// never be prompted or write anything at all.
///
/// Matches the live form by the YAML's own `formId` when it has one (immune
/// to a rename or a name shared with another form; a stale one is an error),
/// or otherwise by table + name + type (`type` absent = Main). A hand-written
/// file that matches nothing is created — shown in the plan as
/// `+ form '&lt;name&gt;' (&lt;type&gt;, create)` and confirmed like any other change —
/// and more than one match is refused, listing the ids to choose from as
/// `formId` (see <see cref="IFormImportService"/>). Refuses outright for a
/// dashboard, same as `build-xml`. `--solution` adds whatever is created or
/// changed to that solution.
///
/// Publishes the form's owning table immediately after writing it (unless
/// <c>--no-publish</c>) — a separate <see cref="IFormImportService.PublishAsync"/>
/// call (see <see cref="Services.Dataverse.IDataverseClient.PublishEntityAsync"/>)
/// that <see cref="ImportRunner"/> makes right after <c>ApplyAsync</c> — so
/// the change is visible to end users without a separate manual publish
/// step.
///
/// What this doesn't do yet: detect that the live form changed since this
/// YAML was last exported (only that it differs from what's about to be
/// written) — see `docs/yaml-conventions.md`.
///
/// The shared preview → diff → confirm → apply flow itself lives in the
/// injected <see cref="ImportRunner"/>, alongside `table import`/`view
/// import` — this class supplies only what's actually different about a
/// form: how to read/preview/apply it, its extra
/// `--allow-schema-violations` gate, and its own exceptions.
/// </summary>
public sealed class ImportFormCommand(IFormImportService formImportService, ImportRunner importRunner)
    : AsyncCommand<ImportFormCommand.Settings>
{
    public sealed class Settings : PublishableImportSettings
    {
        [CommandOption("-s|--solution <UNIQUE_NAME>")]
        [Description("Unique name of a solution to add the form to as it is created or changed.")]
        public string? Solution { get; init; }

        [CommandOption("--allow-schema-violations")]
        [Description("Proceed even if the rebuilt FormXML has a schema violation Dataverse might reject outright — see FormXmlValidationMessage.IsKnownHarmless. Off by default: a genuine 'invalid child element'/'invalid content' violation has been confirmed live to fail the write with a raw Dataverse 400, not just a cosmetic warning.")]
        public bool AllowSchemaViolations { get; init; }

        [CommandOption("--allow-removals")]
        [Description("Remove what's on the live form but missing from the YAML (and not just moved). Off by default: it's kept and listed as 'would remove', so deleting something from the YAML never deletes it from the form by accident.")]
        public bool AllowRemovals { get; init; }

        [CommandOption("--allow-unexpected")]
        [Description("Proceed even if the rebuilt FormXML changes something outside what the YAML changed (a dropped spacer, a regenerated id, a header the YAML left alone). Off by default: that is a bug in this tool, not your edit.")]
        public bool AllowUnexpected { get; init; }
    }

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        RunAsync(settings, cancellationToken);

    /// <summary>
    /// Builds this command's <see cref="ImportFlowSpec{TSettings,TInput,TPreview}"/>: the parts of the shared
    /// preview → diff → confirm → apply flow that differ for this asset type. Shared by
    /// <see cref="RunAsync"/> and <see cref="PreviewAsync(Settings, bool, CancellationToken)"/> (used by <c>solution import</c>).
    /// </summary>
    private ImportFlowSpec<Settings, FormDefinition, FormImportPreview> BuildSpec(Settings settings)
    {
        var spec = new ImportFlowSpec<Settings, FormDefinition, FormImportPreview>
        {
            ReadInputAsync = (s, ct) => YamlFileReader.TryReadAsync(s.Input, "form", FormYamlDeserializer.FromYaml, ct),

            SubjectName = form => form.Name,

            PreviewStatusMessage = form => $"Looking up '{form.Name}' and rebuilding its FormXML...",

            OnPreviewBuilt = preview =>
            {
                if (preview.IdentityMismatchWarning is not null)
                {
                    ErrorConsole.Warn($"Warning: {preview.IdentityMismatchWarning}");
                    AnsiConsole.WriteLine();
                }
            },

            // Confirmed live: StandardFormControls.Resolve deterministically
            // prefers Control over CustomControlId when a control has both
            // set, so a stray CustomControlId alongside an already-correct
            // Control never changes the *rendered* FormXML — HasChanges
            // alone would report "nothing to import" and never let
            // PrintChanges/BlockReason below even look at the violation
            // FormControlValidator already caught, leaving a genuinely
            // broken control definition unreported indefinitely as long as
            // nothing else about the form changes in the same edit.
            // FormImportService therefore always runs FormControlValidator
            // (only the FormXML schema validator is skipped on a no-op), and a
            // blocking violation (not one that's IsKnownHarmless) keeps this
            // from skipping, regardless of
            // HasChanges.
            SkipBeforePrinting = preview => preview.HasChanges || preview.Violations.Any(v => !v.IsKnownHarmless)
                ? null
                : preview.PatchReport?.WouldRemove is { Count: > 0 } kept
                    ? $"the rebuilt FormXML already matches what's live in Dataverse. Nothing to import. {kept.Count} item(s) live but missing from the YAML were kept: {string.Join(", ", kept)} — pass --allow-removals to remove them."
                    : "the rebuilt FormXML already matches what's live in Dataverse. Nothing to import.",

            PrintChanges = preview =>
            {
                if (preview.IsCreate)
                {
                    AnsiConsole.MarkupLine($"[green]{$"+ form '{preview.Name}' ({FormJsonDefinitionReader.FormTypeLabel(preview.FormType).ToLowerInvariant()}, create)".EscapeMarkup()}[/]");
                }

                var existingText = preview.IsCreate ? "" : DiffConsole.PrettyPrintXml(preview.ExistingComparableFormXml);
                DiffConsole.PrintDiff(TextDiff.Compute(existingText, DiffConsole.PrettyPrintXml(preview.NewFormXml)));
                AnsiConsole.WriteLine();

                FormXmlValidationConsole.PrintViolations(preview.Violations);
                PrintPatchReport(preview.PatchReport, settings.AllowRemovals);
            },

            BlockReason = (s, preview) =>
            {
                if (preview.PatchReport?.Unexpected is { Count: > 0 } unexpected && !s.AllowUnexpected)
                {
                    return $"{unexpected.Count} change(s) above are outside what the YAML changed, so this tool would alter parts of the form nobody asked it to. That's a bug in the tool, not your edit — please report it. Pass [bold]--allow-unexpected[/] to proceed anyway once you've checked them yourself.";
                }

                var blockingViolations = preview.Violations.Where(v => !v.IsKnownHarmless).ToList();
                if (blockingViolations.Count == 0 || s.AllowSchemaViolations)
                {
                    return null;
                }

                return $"{blockingViolations.Count} schema violation(s) above aren't a confirmed-safe pattern — two that looked similarly harmless have already failed live with a raw Dataverse 400 ('parameters' rejecting an invalid child element; a control's missing ClassId), so this is blocked by default rather than left to a human to eyeball correctly every time. Pass [bold]--allow-schema-violations[/] to proceed anyway once you've checked these yourself.";
            },

            ApplyStatusMessage = "Importing...",

            ApplyAsync = (auth, preview, ct) => formImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, settings.Solution, ct),

            PrintSuccess = (form, preview) => AnsiConsole.MarkupLine($"[green]Imported.[/] '{form.Name.EscapeMarkup()}' {(preview.IsCreate ? "created" : "updated")} in Dataverse."),

            PublishAsync = (auth, preview, ct) => formImportService.PublishAsync(auth.EnvironmentUrl, auth.AccessToken, preview, ct),

            FormatDomainException = (ex, form) => ex switch
            {
                FormNotFoundException or AmbiguousSystemFormException or NotSupportedException or InvalidDataException => $"[red]{ex.Message.EscapeMarkup()}[/]",
                _ => null,
            },
        };

        return spec;
    }

    public Task<int> RunAsync(Settings settings, CancellationToken cancellationToken) =>
        importRunner.RunAsync(settings, new RemovalAwareImport(formImportService, settings.AllowRemovals), BuildSpec(settings), cancellationToken);

    /// <summary>
    /// Previews without prompting or writing: the first half of
    /// <see cref="RunAsync"/>, for callers (<c>solution import</c>) that
    /// collect several previews and confirm once.
    /// </summary>
    public Task<PreparedImport> PreviewAsync(Settings settings, bool printUnchanged, CancellationToken cancellationToken) =>
        importRunner.PreviewAsync(settings, new RemovalAwareImport(formImportService, settings.AllowRemovals), BuildSpec(settings), printUnchanged, cancellationToken);

    /// <summary>
    /// What's live but missing from the YAML (kept, or removed with
    /// --allow-removals), and anything the patch changed that the YAML
    /// didn't ask for (which blocks the import, see BlockReason).
    /// </summary>
    private static void PrintPatchReport(FormPatchReport? report, bool allowRemovals)
    {
        if (report is null)
        {
            return;
        }

        foreach (var item in report.WouldRemove)
        {
            AnsiConsole.MarkupLine(allowRemovals
                ? $"[red]- remove {item.EscapeMarkup()}[/] (live, missing from the YAML)"
                : $"[yellow]~ would remove {item.EscapeMarkup()}[/] (live, missing from the YAML) — kept; pass --allow-removals to remove it");
        }

        if (report.Unexpected.Count > 0)
        {
            AnsiConsole.MarkupLine($"[red]{report.Unexpected.Count} change(s) outside what the YAML changed:[/]");
            foreach (var difference in report.Unexpected.Take(20))
            {
                AnsiConsole.MarkupLine($"  [red]{$"{difference.Kind} {difference.Path} [{difference.Before}] -> [{difference.After}]".EscapeMarkup()}[/]");
            }

            if (report.Unexpected.Count > 20)
            {
                AnsiConsole.MarkupLine($"  [red]... and {report.Unexpected.Count - 20} more[/]");
            }
        }

        if (report.WouldRemove.Count > 0 || report.Unexpected.Count > 0)
        {
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>Passes <c>--allow-removals</c> through to the preview, which the shared import runner doesn't know about.</summary>
    private sealed class RemovalAwareImport(IFormImportService inner, bool allowRemovals) : IImportService<FormDefinition, FormImportPreview>
    {
        public Task<FormImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, FormDefinition input, CancellationToken cancellationToken) =>
            inner.PreviewAsync(environmentUrl, accessToken, input, allowRemovals, cancellationToken);

        public Task ApplyAsync(Uri environmentUrl, string accessToken, FormImportPreview preview, CancellationToken cancellationToken) =>
            inner.ApplyAsync(environmentUrl, accessToken, preview, cancellationToken);
    }
}

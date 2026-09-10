using System.ComponentModel;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using Spectre.Console;
using Spectre.Console.Cli;

namespace D365Architect.Commands.Form;

/// <summary>
/// `d365architect form import --input account-main-form.form.yml [--yes] [--whatif]`
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
/// Only ever updates a form that already exists — matched by the YAML's own
/// `formId` when it has one (immune to a rename or a name shared with
/// another form), or by table + name as a fallback for a file exported
/// before that field existed. Refuses (rather than creating one) when
/// nothing matches, and refuses outright for a dashboard, same as
/// `build-xml`.
///
/// Publishes the form's owning table immediately after writing it — a
/// separate <see cref="IFormImportService.PublishAsync"/> call this class
/// itself makes right after <c>ApplyAsync</c> (see
/// <see cref="Services.Dataverse.IDataverseClient.PublishEntityAsync"/>) —
/// so the change is visible to end users without a separate manual publish
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
    public sealed class Settings : ImportSettingsBase
    {
        [CommandOption("--allow-schema-violations")]
        [Description("Proceed even if the rebuilt FormXML has a schema violation Dataverse might reject outright — see FormXmlValidationMessage.IsKnownHarmless. Off by default: a genuine 'invalid child element'/'invalid content' violation has been confirmed live to fail the write with a raw Dataverse 400, not just a cosmetic warning.")]
        public bool AllowSchemaViolations { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
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
                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] {preview.IdentityMismatchWarning.EscapeMarkup()}");
                    AnsiConsole.WriteLine();
                }
            },

            SkipBeforePrinting = preview => preview.HasChanges
                ? null
                : "the rebuilt FormXML already matches what's live in Dataverse. Nothing to import.",

            PrintChanges = preview =>
            {
                DiffConsole.PrintDiff(TextDiff.Compute(DiffConsole.PrettyPrintXml(preview.ExistingComparableFormXml), DiffConsole.PrettyPrintXml(preview.NewFormXml)));
                AnsiConsole.WriteLine();

                FormXmlValidationConsole.PrintViolations(preview.Violations);
            },

            BlockReason = (s, preview) =>
            {
                var blockingViolations = preview.Violations.Where(v => !v.IsKnownHarmless).ToList();
                if (blockingViolations.Count == 0 || s.AllowSchemaViolations)
                {
                    return null;
                }

                return $"{blockingViolations.Count} schema violation(s) above aren't a confirmed-safe pattern — two that looked similarly harmless have already failed live with a raw Dataverse 400 ('parameters' rejecting an invalid child element; a control's missing ClassId), so this is blocked by default rather than left to a human to eyeball correctly every time. Pass [bold]--allow-schema-violations[/] to proceed anyway once you've checked these yourself.";
            },

            ApplyStatusMessage = "Importing and publishing...",

            ApplyAsync = async (auth, preview, ct) =>
            {
                // PublishAsync is its own call, separate from ApplyAsync's
                // shared write-only shape — see IFormImportService's own
                // doc comment for why publishing doesn't fit
                // IImportService<TInput,TPreview> the way table/view import
                // (which never publish) do.
                await formImportService.ApplyAsync(auth.EnvironmentUrl, auth.AccessToken, preview, ct);
                await formImportService.PublishAsync(auth.EnvironmentUrl, auth.AccessToken, preview, ct);
            },

            PrintSuccess = (form, preview) => AnsiConsole.MarkupLine($"[green]Imported and published.[/] '{form.Name}' updated in Dataverse."),

            FormatDomainException = (ex, form) => ex switch
            {
                FormNotFoundException or AmbiguousSystemFormException or NotSupportedException => $"[red]{ex.Message.EscapeMarkup()}[/]",
                _ => null,
            },
        };

        return await importRunner.RunAsync(settings, formImportService, spec, cancellationToken);
    }
}

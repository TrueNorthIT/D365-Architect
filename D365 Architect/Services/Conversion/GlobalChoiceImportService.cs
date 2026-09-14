using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class GlobalChoiceImportService(IDataverseClient dataverseClient) : IGlobalChoiceImportService
{
    public async Task<GlobalChoicesImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, IReadOnlyList<GlobalChoiceDefinition> locals, CancellationToken cancellationToken)
    {
        var plans = new List<GlobalChoiceImportPlan>();
        var existingChoices = new List<GlobalChoiceDefinition>();

        // Purely local, before any live lookup — same class of check as
        // TableImportService's own duplicateSchemaNames/
        // duplicateRelationshipSchemaNames: two entries in this input
        // claiming the same Name would each independently look up (and then
        // plan to create/update) the identical live choice, and only collide
        // when Dataverse rejects the second write. Unlike those two checks,
        // this one doesn't need to be scoped to "doesn't exist live yet"
        // first — Name is a global choice's own lookup key, so a duplicate
        // here is meaningless regardless of what's live, and catching it
        // up front also means never bothering with a live lookup for an
        // entry already known to be invalid.
        var duplicateNames = locals
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var local in locals)
        {
            if (duplicateNames.Contains(local.Name))
            {
                plans.Add(new GlobalChoiceImportPlan(local.Name, GlobalChoiceImportAction.Invalid,
                    $"Name '{local.Name}' is used by more than one choice in this input — Dataverse requires it to be unique.", null, null));
                continue;
            }

            var existingJson = await dataverseClient.TryGetGlobalOptionSetJsonAsync(environmentUrl, accessToken, local.Name, cancellationToken);

            if (existingJson is null)
            {
                var validationError = GlobalChoiceChangeValidator.ValidateCreate(local);
                plans.Add(validationError is not null
                    ? new GlobalChoiceImportPlan(local.Name, GlobalChoiceImportAction.Invalid, validationError, null, null)
                    : new GlobalChoiceImportPlan(local.Name, GlobalChoiceImportAction.Create, null, GlobalChoiceMetadataJsonBuilder.BuildCreateBody(local), null));
                continue;
            }

            var existing = GlobalChoiceJsonReader.Read(existingJson);
            existingChoices.Add(existing);

            var optionChanges = GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans(local, existing);
            var baseFieldsChanged = !BaseFieldsMatch(local, existing);

            if (!baseFieldsChanged && optionChanges.Count == 0)
            {
                plans.Add(new GlobalChoiceImportPlan(local.Name, GlobalChoiceImportAction.Unchanged, null, null, null));
                continue;
            }

            JsonObject? updateBody = null;
            if (baseFieldsChanged)
            {
                updateBody = JsonNode.Parse(existingJson)!.AsObject();
                GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields(updateBody, local);
            }

            plans.Add(new GlobalChoiceImportPlan(local.Name, GlobalChoiceImportAction.Update, null, updateBody,
                optionChanges.Count > 0 ? optionChanges : null));
        }

        var existingYaml = GlobalChoiceYamlSerializer.ToYaml(existingChoices);
        var newYaml = GlobalChoiceYamlSerializer.ToYaml(locals);

        return new GlobalChoicesImportPreview(existingYaml, newYaml, plans);
    }

    public async Task ApplyAsync(Uri environmentUrl, string accessToken, GlobalChoicesImportPreview preview, CancellationToken cancellationToken)
    {
        foreach (var plan in preview.ChoicePlans)
        {
            switch (plan.Action)
            {
                case GlobalChoiceImportAction.Create:
                    await dataverseClient.CreateGlobalOptionSetAsync(environmentUrl, accessToken, plan.RequestBody!, cancellationToken);
                    break;

                case GlobalChoiceImportAction.Update when plan.RequestBody is not null:
                    var metadataId = plan.RequestBody["MetadataId"]!.GetValue<Guid>();
                    await dataverseClient.UpdateGlobalOptionSetAsync(environmentUrl, accessToken, metadataId, plan.RequestBody, cancellationToken);
                    break;

                // Unchanged/Invalid, and an Update with no base-field change
                // (options only): nothing to do here, by design.
            }

            if (plan.OptionChanges is not null)
            {
                foreach (var change in plan.OptionChanges)
                {
                    await OptionChangeApplier.ApplyAsync(dataverseClient, environmentUrl, accessToken, change, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// True when every field <paramref name="local"/> actually specifies
    /// matches <paramref name="existing"/> — a null field on
    /// <paramref name="local"/> always "matches" (means "don't touch this"),
    /// same convention as <see cref="TableImportService"/>'s own
    /// AttributesMatch. Options are deliberately not part of this — they're
    /// diffed separately by <see cref="GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans"/>,
    /// which already handles "nothing to do" on its own (an empty plan
    /// list).
    /// </summary>
    private static bool BaseFieldsMatch(GlobalChoiceDefinition local, GlobalChoiceDefinition existing) =>
        (local.DisplayName is null || local.DisplayName == existing.DisplayName)
        && (local.Description is null || local.Description == existing.Description);
}

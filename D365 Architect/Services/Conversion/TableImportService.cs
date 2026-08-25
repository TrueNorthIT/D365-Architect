using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class TableImportService(IDataverseClient dataverseClient, EntityJsonDefinitionReader reader) : ITableImportService
{
    public async Task<TableImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, EntityDefinition entity, CancellationToken cancellationToken)
    {
        var existingJson = await dataverseClient.GetEntityDefinitionJsonAsync(environmentUrl, accessToken, entity.LogicalName, cancellationToken);
        var existingEntity = reader.Read(existingJson);

        var existingYaml = EntityYamlSerializer.ToYaml(existingEntity);
        var newYaml = EntityYamlSerializer.ToYaml(entity);

        var tableUpdateBody = await BuildTableUpdateBodyAsync(environmentUrl, accessToken, entity, existingEntity, cancellationToken);
        var attributePlans = await BuildAttributePlansAsync(environmentUrl, accessToken, entity, existingEntity, cancellationToken);

        return new TableImportPreview(entity.LogicalName, existingYaml, newYaml, tableUpdateBody, attributePlans);
    }

    public async Task ApplyAsync(Uri environmentUrl, string accessToken, TableImportPreview preview, CancellationToken cancellationToken)
    {
        if (preview.TableUpdateBody is not null)
        {
            await dataverseClient.UpdateEntityAsync(environmentUrl, accessToken, preview.EntityLogicalName, preview.TableUpdateBody, cancellationToken);
        }

        foreach (var plan in preview.AttributePlans)
        {
            switch (plan.Action)
            {
                case AttributeImportAction.Create:
                    await dataverseClient.CreateAttributeAsync(environmentUrl, accessToken, preview.EntityLogicalName, plan.RequestBody!, cancellationToken);
                    break;

                case AttributeImportAction.Update:
                    await dataverseClient.UpdateAttributeAsync(environmentUrl, accessToken, preview.EntityLogicalName, plan.LogicalName, plan.RequestBody!, cancellationToken);
                    break;

                // Unchanged/SkippedUnsupportedType/WouldRemove/Invalid: nothing to do, by design.
            }
        }
    }

    /// <summary>
    /// Only set when <paramref name="local"/> actually specifies a
    /// DisplayName/PluralDisplayName/Description that differs from
    /// <paramref name="existing"/> — a null field on <paramref name="local"/>
    /// means "don't touch this", never "reset to some default", so it's
    /// never compared at all.
    /// </summary>
    private async Task<JsonObject?> BuildTableUpdateBodyAsync(Uri environmentUrl, string accessToken, EntityDefinition local, EntityDefinition existing, CancellationToken cancellationToken)
    {
        var displayNameChanged = local.DisplayName is not null && local.DisplayName != existing.DisplayName;
        var pluralChanged = local.PluralDisplayName is not null && local.PluralDisplayName != existing.PluralDisplayName;
        var descriptionChanged = local.Description is not null && local.Description != existing.Description;

        if (!displayNameChanged && !pluralChanged && !descriptionChanged)
        {
            return null;
        }

        var json = await dataverseClient.GetEntityMetadataJsonAsync(environmentUrl, accessToken, local.LogicalName, cancellationToken);
        var entityMetadata = JsonNode.Parse(json)!.AsObject();

        if (displayNameChanged)
        {
            entityMetadata["DisplayName"] = DataverseLabelJson.Build(local.DisplayName!);
        }

        if (pluralChanged)
        {
            entityMetadata["DisplayCollectionName"] = DataverseLabelJson.Build(local.PluralDisplayName!);
        }

        if (descriptionChanged)
        {
            entityMetadata["Description"] = DataverseLabelJson.Build(local.Description!);
        }

        return entityMetadata;
    }

    private async Task<IReadOnlyList<AttributeImportPlan>> BuildAttributePlansAsync(Uri environmentUrl, string accessToken, EntityDefinition local, EntityDefinition existing, CancellationToken cancellationToken)
    {
        var plans = new List<AttributeImportPlan>();
        var existingByName = existing.Attributes.ToDictionary(a => a.Name);
        var localNames = new HashSet<string>(local.Attributes.Select(a => a.Name));

        // Only checked among attributes that don't exist live yet — a
        // SchemaName collision against something already live is already
        // caught per-attribute below (it just shows up as a Type/SchemaName
        // mismatch or an ordinary Update), but two new columns in the same
        // local YAML both claiming the same SchemaName would otherwise sail
        // through individually and only fail when Dataverse rejects the
        // second create — a purely local, cross-attribute check
        // AttributeChangeValidator can't do on its own since it only ever
        // sees one attribute at a time.
        var duplicateSchemaNames = local.Attributes
            .Where(a => !existingByName.ContainsKey(a.Name) && a.SchemaName is not null)
            .GroupBy(a => a.SchemaName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var localAttribute in local.Attributes)
        {
            if (!existingByName.TryGetValue(localAttribute.Name, out var existingAttribute))
            {
                if (localAttribute.SchemaName is not null && duplicateSchemaNames.Contains(localAttribute.SchemaName))
                {
                    plans.Add(new AttributeImportPlan(localAttribute.Name, AttributeImportAction.Invalid,
                        $"SchemaName '{localAttribute.SchemaName}' is used by more than one new column in this YAML — Dataverse requires it to be unique.", null));
                    continue;
                }

                plans.Add(await BuildCreatePlanAsync(localAttribute));
                continue;
            }

            // Checked before AttributesMatch, and before AttributeChangeValidator,
            // deliberately: Type/SchemaName aren't in AttributesMatch's own
            // field list at all (this tool never writes either one back for
            // an existing column, so they'd otherwise never be compared) —
            // without this, a type or schema-name change alongside no other
            // difference would be silently reported as "Unchanged" instead
            // of the invalid, would-fail change it actually is.
            if (localAttribute.Type != existingAttribute.Type)
            {
                plans.Add(new AttributeImportPlan(localAttribute.Name, AttributeImportAction.Invalid,
                    $"Can't change type from '{existingAttribute.Type}' to '{localAttribute.Type}' — Dataverse doesn't support changing a column's data type after creation.", null));
                continue;
            }

            if (localAttribute.SchemaName is not null && localAttribute.SchemaName != existingAttribute.SchemaName)
            {
                plans.Add(new AttributeImportPlan(localAttribute.Name, AttributeImportAction.Invalid,
                    $"Can't change SchemaName from '{existingAttribute.SchemaName}' to '{localAttribute.SchemaName}' — immutable after creation.", null));
                continue;
            }

            if (AttributesMatch(localAttribute, existingAttribute))
            {
                plans.Add(new AttributeImportPlan(localAttribute.Name, AttributeImportAction.Unchanged, null, null));
                continue;
            }

            plans.Add(await BuildUpdatePlanAsync(environmentUrl, accessToken, local.LogicalName, localAttribute, existingAttribute, cancellationToken));
        }

        foreach (var existingAttribute in existing.Attributes)
        {
            if (!localNames.Contains(existingAttribute.Name))
            {
                plans.Add(new AttributeImportPlan(existingAttribute.Name, AttributeImportAction.WouldRemove,
                    "Present live but not in the local YAML — this tool never deletes columns automatically.", null));
            }
        }

        return plans;
    }

    private static Task<AttributeImportPlan> BuildCreatePlanAsync(AttributeDefinition local)
    {
        if (!AttributeMetadataJsonBuilder.SupportedTypes.Contains(local.Type))
        {
            return Task.FromResult(new AttributeImportPlan(local.Name, AttributeImportAction.SkippedUnsupportedType,
                $"Creating a new '{local.Type}' column isn't supported yet.", null));
        }

        var validationError = AttributeChangeValidator.ValidateCreate(local);
        if (validationError is not null)
        {
            return Task.FromResult(new AttributeImportPlan(local.Name, AttributeImportAction.Invalid, validationError, null));
        }

        JsonObject body;
        try
        {
            body = AttributeMetadataJsonBuilder.BuildCreateBody(local);
        }
        catch (InvalidOperationException ex)
        {
            // Defensive fallback only — ValidateCreate already checks the
            // one thing this can throw for (a missing SchemaName).
            return Task.FromResult(new AttributeImportPlan(local.Name, AttributeImportAction.Invalid, ex.Message, null));
        }

        return Task.FromResult(new AttributeImportPlan(local.Name, AttributeImportAction.Create, null, body));
    }

    private async Task<AttributeImportPlan> BuildUpdatePlanAsync(Uri environmentUrl, string accessToken, string entityLogicalName, AttributeDefinition local, AttributeDefinition existing, CancellationToken cancellationToken)
    {
        if (!AttributeMetadataJsonBuilder.SupportedTypes.Contains(existing.Type))
        {
            return new AttributeImportPlan(local.Name, AttributeImportAction.SkippedUnsupportedType,
                $"Updating a '{existing.Type}' column isn't supported yet.", null);
        }

        var validationError = AttributeChangeValidator.ValidateUpdate(local, existing);
        if (validationError is not null)
        {
            return new AttributeImportPlan(local.Name, AttributeImportAction.Invalid, validationError, null);
        }

        var json = await dataverseClient.GetAttributeMetadataJsonAsync(environmentUrl, accessToken, entityLogicalName, local.Name, existing.Type, cancellationToken);
        var attributeMetadata = JsonNode.Parse(json)!.AsObject();
        AttributeMetadataJsonBuilder.ApplyUpdateFields(attributeMetadata, local);

        var warnings = AttributeChangeValidator.Warnings(local, existing);
        return new AttributeImportPlan(local.Name, AttributeImportAction.Update, null, attributeMetadata, warnings.Count > 0 ? warnings : null);
    }

    /// <summary>
    /// True when every field <paramref name="local"/> actually specifies
    /// matches <paramref name="existing"/> — a null field on
    /// <paramref name="local"/> always "matches" (means "don't touch this",
    /// never "should be cleared/defaulted"), so it's never compared.
    /// </summary>
    private static bool AttributesMatch(AttributeDefinition local, AttributeDefinition existing) =>
        FieldMatches(local.DisplayName, existing.DisplayName)
        && FieldMatches(local.Description, existing.Description)
        && FieldMatches(local.RequiredLevel, existing.RequiredLevel)
        && FieldMatches(local.MaxLength, existing.MaxLength)
        && FieldMatches(local.Precision, existing.Precision)
        && FieldMatches(local.PrecisionSource, existing.PrecisionSource)
        && FieldMatches(local.MinValue, existing.MinValue)
        && FieldMatches(local.MaxValue, existing.MaxValue)
        && FieldMatches(local.Format, existing.Format);

    private static bool FieldMatches<T>(T? local, T? existing) => local is null || EqualityComparer<T>.Default.Equals(local, existing);
}

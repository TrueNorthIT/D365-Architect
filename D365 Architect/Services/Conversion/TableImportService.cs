using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class TableImportService(IDataverseClient dataverseClient, EntityJsonDefinitionReader reader, AttributeOptionSetFetcher optionSetFetcher) : ITableImportService
{
    public async Task<TableImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, EntityDefinition entity, CancellationToken cancellationToken)
    {
        var existingJson = await dataverseClient.GetEntityDefinitionJsonAsync(environmentUrl, accessToken, entity.LogicalName, cancellationToken);
        var existingOptionSetJsonByAttribute = await optionSetFetcher.FetchAsync(environmentUrl, accessToken, entity.LogicalName, existingJson, cancellationToken);
        var existingEntity = reader.Read(existingJson, allowedAttributeMetadataIds: null, existingOptionSetJsonByAttribute);

        var existingYaml = EntityYamlSerializer.ToYaml(existingEntity);
        var newYaml = EntityYamlSerializer.ToYaml(entity);

        var tableUpdateBody = await BuildTableUpdateBodyAsync(environmentUrl, accessToken, entity, existingEntity, cancellationToken);
        var attributePlans = await BuildAttributePlansAsync(environmentUrl, accessToken, entity, existingEntity, cancellationToken);

        return new TableImportPreview(entity.LogicalName, existingYaml, newYaml, tableUpdateBody, attributePlans);
    }

    public Task ApplyAsync(Uri environmentUrl, string accessToken, TableImportPreview preview, CancellationToken cancellationToken) =>
        ApplyAsync(environmentUrl, accessToken, preview, useTransaction: true, solutionUniqueName: null, cancellationToken);

    public async Task ApplyAsync(Uri environmentUrl, string accessToken, TableImportPreview preview, bool useTransaction, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        if (useTransaction)
        {
            var writes = BuildWrites(preview, solutionUniqueName);
            if (writes.Count > 0)
            {
                await dataverseClient.ExecuteTransactionAsync(environmentUrl, accessToken, writes, cancellationToken);
            }

            return;
        }

        if (preview.TableUpdateBody is not null)
        {
            await dataverseClient.UpdateEntityAsync(environmentUrl, accessToken, preview.EntityLogicalName, preview.TableUpdateBody, cancellationToken);
        }

        foreach (var plan in preview.AttributePlans)
        {
            switch (plan.Action)
            {
                case AttributeImportAction.Create:
                    await dataverseClient.CreateAttributeAsync(environmentUrl, accessToken, preview.EntityLogicalName, plan.RequestBody!, solutionUniqueName, cancellationToken);
                    break;

                case AttributeImportAction.Update:
                    await dataverseClient.UpdateAttributeAsync(environmentUrl, accessToken, preview.EntityLogicalName, plan.LogicalName, plan.RequestBody!, cancellationToken);
                    break;

                case AttributeImportAction.CreateLookupRelationship:
                    await dataverseClient.CreateOneToManyRelationshipAsync(environmentUrl, accessToken, plan.RequestBody!, solutionUniqueName, cancellationToken);
                    break;

                case AttributeImportAction.CreateCustomerRelationship:
                    await dataverseClient.CreateCustomerRelationshipsAsync(environmentUrl, accessToken, plan.RequestBody!, solutionUniqueName, cancellationToken);
                    break;

                // Unchanged/SkippedUnsupportedType/WouldRemove/Invalid: nothing to do, by design.
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
    /// The same writes the <c>useTransaction: false</c> loop above makes one
    /// at a time, in the same order, but as declarative <see cref="DataverseWrite"/>
    /// values for <see cref="IDataverseClient.ExecuteTransactionAsync"/> to
    /// send as one changeset instead. <paramref name="solutionUniqueName"/>
    /// is only ever attached to a <em>create</em> case — see
    /// <see cref="ApplyAsync(Uri, string, TableImportPreview, bool, string?, CancellationToken)"/>'s
    /// own doc comment.
    /// </summary>
    private static List<DataverseWrite> BuildWrites(TableImportPreview preview, string? solutionUniqueName)
    {
        var writes = new List<DataverseWrite>();

        if (preview.TableUpdateBody is not null)
        {
            writes.Add(new DataverseWrite.UpdateEntity(preview.EntityLogicalName, preview.TableUpdateBody));
        }

        foreach (var plan in preview.AttributePlans)
        {
            switch (plan.Action)
            {
                case AttributeImportAction.Create:
                    writes.Add(new DataverseWrite.CreateAttribute(preview.EntityLogicalName, plan.RequestBody!, solutionUniqueName));
                    break;

                case AttributeImportAction.Update:
                    writes.Add(new DataverseWrite.UpdateAttribute(preview.EntityLogicalName, plan.LogicalName, plan.RequestBody!));
                    break;

                case AttributeImportAction.CreateLookupRelationship:
                    writes.Add(new DataverseWrite.CreateOneToManyRelationship(plan.RequestBody!, solutionUniqueName));
                    break;

                case AttributeImportAction.CreateCustomerRelationship:
                    writes.Add(new DataverseWrite.CreateCustomerRelationships(plan.RequestBody!, solutionUniqueName));
                    break;

                // Unchanged/SkippedUnsupportedType/WouldRemove/Invalid: nothing to do, by design.
            }

            if (plan.OptionChanges is not null)
            {
                foreach (var change in plan.OptionChanges)
                {
                    writes.Add(OptionChangeApplier.ToWrite(change));
                }
            }
        }

        return writes;
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

        // Same reasoning as duplicateSchemaNames above, for a new Lookup
        // column's own RelationshipSchemaName (the schema name of the
        // one-to-many relationship that owns it — see
        // AttributeDefinition.RelationshipSchemaName) instead of the column's
        // SchemaName: two new Lookups with different SchemaNames but the same
        // RelationshipSchemaName would both plan as CreateLookupRelationship
        // and only collide when Dataverse rejects the second
        // RelationshipDefinitions POST live.
        var duplicateRelationshipSchemaNames = local.Attributes
            .Where(a => !existingByName.ContainsKey(a.Name) && a.Type == "Lookup" && a.RelationshipSchemaName is not null)
            .GroupBy(a => a.RelationshipSchemaName, StringComparer.OrdinalIgnoreCase)
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

                if (localAttribute.RelationshipSchemaName is not null && duplicateRelationshipSchemaNames.Contains(localAttribute.RelationshipSchemaName))
                {
                    plans.Add(new AttributeImportPlan(localAttribute.Name, AttributeImportAction.Invalid,
                        $"RelationshipSchemaName '{localAttribute.RelationshipSchemaName}' is used by more than one new Lookup column in this YAML — Dataverse requires it to be unique.", null));
                    continue;
                }

                plans.Add(await BuildCreatePlanAsync(environmentUrl, accessToken, local.LogicalName, localAttribute, cancellationToken));
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

            // Same reasoning as the Type/SchemaName checks above: Targets
            // isn't in AttributesMatch's field list either (this tool never
            // writes it back for an existing Lookup/Customer/Owner column),
            // so a changed Targets would otherwise be silently ignored
            // rather than caught as the invalid, would-fail change it is.
            if (localAttribute.Type is "Lookup" or "Customer" or "Owner"
                && localAttribute.Targets is not null
                && !TargetsMatch(localAttribute.Targets, existingAttribute.Targets))
            {
                plans.Add(new AttributeImportPlan(localAttribute.Name, AttributeImportAction.Invalid,
                    $"Can't change Targets on an existing {localAttribute.Type} column — immutable after creation.", null));
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

    private static bool TargetsMatch(IReadOnlyList<string> local, IReadOnlyList<string>? existing) =>
        existing is not null && local.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(existing);

    /// <summary>
    /// Owner/State/Status are created automatically with every table and can
    /// never be created via this tool; Lookup/Customer are created a
    /// structurally different way (a relationship, not a plain attribute
    /// POST) — see <see cref="AttributeMetadataJsonBuilder.BuildRelationshipCreateBody"/>/
    /// <see cref="AttributeMetadataJsonBuilder.BuildCustomerRelationshipCreateBody"/>.
    /// Everything else routes through the ordinary
    /// <see cref="AttributeMetadataJsonBuilder.CreatableTypes"/>-gated path.
    /// Has to be async (unlike every other branch here, which needs nothing
    /// from Dataverse to plan) for exactly one case: a new Picklist/
    /// MultiSelectPicklist bound to an existing global choice needs that
    /// choice's own live MetadataId before <see cref="AttributeMetadataJsonBuilder.BuildCreateBody"/>
    /// can build a working bind — see that method's own doc comment on
    /// <c>globalOptionSetMetadataId</c> for why a Name-based bind doesn't
    /// work here.
    /// </summary>
    private async Task<AttributeImportPlan> BuildCreatePlanAsync(Uri environmentUrl, string accessToken, string entityLogicalName, AttributeDefinition local, CancellationToken cancellationToken)
    {
        if (local.Type is "Owner" or "State" or "Status")
        {
            return new AttributeImportPlan(local.Name, AttributeImportAction.Invalid,
                $"'{local.Type}' columns are created automatically with every table and can't be created via this tool.", null);
        }

        if (local.Type is "Lookup" or "Customer")
        {
            var relationshipValidationError = AttributeChangeValidator.ValidateCreate(local);
            if (relationshipValidationError is not null)
            {
                return new AttributeImportPlan(local.Name, AttributeImportAction.Invalid, relationshipValidationError, null);
            }

            try
            {
                return local.Type == "Lookup"
                    ? new AttributeImportPlan(local.Name, AttributeImportAction.CreateLookupRelationship, null, AttributeMetadataJsonBuilder.BuildRelationshipCreateBody(entityLogicalName, local))
                    : new AttributeImportPlan(local.Name, AttributeImportAction.CreateCustomerRelationship, null, AttributeMetadataJsonBuilder.BuildCustomerRelationshipCreateBody(entityLogicalName, local));
            }
            catch (InvalidOperationException ex)
            {
                // Defensive fallback only — ValidateCreate already checks
                // everything this can throw for.
                return new AttributeImportPlan(local.Name, AttributeImportAction.Invalid, ex.Message, null);
            }
        }

        if (!AttributeMetadataJsonBuilder.CreatableTypes.Contains(local.Type))
        {
            return new AttributeImportPlan(local.Name, AttributeImportAction.SkippedUnsupportedType,
                $"Creating a new '{local.Type}' column isn't supported yet.", null);
        }

        var validationError = AttributeChangeValidator.ValidateCreate(local);
        if (validationError is not null)
        {
            return new AttributeImportPlan(local.Name, AttributeImportAction.Invalid, validationError, null);
        }

        // Only Picklist/MultiSelectPicklist can carry a GlobalOptionSetName
        // (see ValidateOptionsForCreate) — resolved live here, rather than
        // inside BuildCreateBody itself, since only this layer has
        // dataverseClient access. A name that doesn't resolve live is a
        // genuine, surfaced error rather than something left for Dataverse's
        // own opaque bind failure to explain.
        Guid? globalOptionSetMetadataId = null;
        if (local.Type is "Picklist" or "MultiSelectPicklist" && local.GlobalOptionSetName is not null)
        {
            var globalChoiceJson = await dataverseClient.TryGetGlobalOptionSetJsonAsync(environmentUrl, accessToken, local.GlobalOptionSetName, cancellationToken);
            if (globalChoiceJson is null)
            {
                return new AttributeImportPlan(local.Name, AttributeImportAction.Invalid,
                    $"Global choice '{local.GlobalOptionSetName}' doesn't exist — create it first via `choice import`.", null);
            }

            globalOptionSetMetadataId = JsonNode.Parse(globalChoiceJson)!["MetadataId"]!.GetValue<Guid>();
        }

        JsonObject body;
        try
        {
            body = AttributeMetadataJsonBuilder.BuildCreateBody(local, globalOptionSetMetadataId);
        }
        catch (InvalidOperationException ex)
        {
            // Defensive fallback only — ValidateCreate already checks the
            // one thing this can throw for (a missing SchemaName), and the
            // globalOptionSetMetadataId case above is already resolved by
            // the time this runs.
            return new AttributeImportPlan(local.Name, AttributeImportAction.Invalid, ex.Message, null);
        }

        return new AttributeImportPlan(local.Name, AttributeImportAction.Create, null, body);
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
        var optionChanges = AttributeMetadataJsonBuilder.BuildOptionChangePlans(entityLogicalName, local, existing);

        return new AttributeImportPlan(local.Name, AttributeImportAction.Update, null, attributeMetadata,
            warnings.Count > 0 ? warnings : null, optionChanges.Count > 0 ? optionChanges : null);
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
        && FieldMatches(local.Format, existing.Format)
        && FieldMatches(local.DefaultValue, existing.DefaultValue)
        && FieldMatches(local.TrueOptionLabel, existing.TrueOptionLabel)
        && FieldMatches(local.FalseOptionLabel, existing.FalseOptionLabel)
        && FieldMatches(local.GlobalOptionSetName, existing.GlobalOptionSetName)
        && OptionsMatch(local.Options, existing.Options);

    private static bool FieldMatches<T>(T? local, T? existing) => local is null || EqualityComparer<T>.Default.Equals(local, existing);

    /// <summary>
    /// Order-sensitive: identical values in a different order still counts
    /// as "not matching" here, so the option-level diff in
    /// <see cref="AttributeMetadataJsonBuilder.BuildOptionChangePlans"/> runs
    /// and can decide whether that's an insert/rename/reorder.
    /// </summary>
    private static bool OptionsMatch(IReadOnlyList<AttributeOptionDefinition>? local, IReadOnlyList<AttributeOptionDefinition>? existing)
    {
        if (local is null)
        {
            return true;
        }

        if (existing is null || local.Count != existing.Count)
        {
            return false;
        }

        return local.Zip(existing, (l, e) => l.Value == e.Value && l.Label == e.Label).All(match => match);
    }
}

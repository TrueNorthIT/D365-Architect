using System.Text.Json.Nodes;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Builds/mutates the JSON bodies Dataverse's Web API needs to create or
/// update a column — confirmed against Microsoft's own documented
/// create/update examples for each type covered, not guessed. See
/// <see cref="SupportedTypes"/>/<see cref="CreatableTypes"/> for exactly
/// which types that is and `docs/yaml-conventions.md`'s "Importing tables"
/// section for why the rest are deliberately excluded rather than attempted
/// anyway.
///
/// Update is a full-object replace, not a partial patch: Dataverse's own
/// docs are explicit that <c>PUT</c> on a column "can't update individual
/// properties" — you must send back the entire current definition with
/// only the fields you actually want changed edited. <see cref="ApplyUpdateFields"/>
/// takes that full live representation (fetched immediately beforehand)
/// and mutates only the fields this tool tracks, in place — the same
/// retrieve-and-patch principle <see cref="Conversion.FormXmlWriter"/>
/// already applies to FormXML, applied here to a JSON object instead of an
/// XML document. Microsoft's own docs confirm this pattern applies
/// generically to every attribute type, not just the ones with type-specific
/// fields to set — that's what lets <see cref="ApplyUpdateFields"/> cover
/// <c>Owner</c>/<c>Lookup</c>/<c>Customer</c>/<c>State</c>/<c>Status</c> with
/// an empty case each (only the shared DisplayName/Description/RequiredLevel
/// fields ever change on those, the same way <c>BigInt</c> already works).
///
/// A choice column's own options are never touched by <see cref="ApplyUpdateFields"/>
/// at all — Dataverse doesn't allow it as part of the attribute PUT; see
/// <see cref="BuildOptionChangePlans"/> for the separate action-based
/// mechanism (<c>InsertOptionValue</c>/<c>UpdateOptionValue</c>/<c>OrderOption</c>/
/// <c>UpdateStateValue</c>) that handles those instead.
///
/// <see cref="ApplyUpdateFields"/> also has to set <c>@odata.type</c> on the
/// cloned body itself, for the same reason
/// <see cref="GlobalChoiceMetadataJsonBuilder.ApplyUpdateFields"/> does
/// (confirmed live there first): <see cref="IDataverseClient.GetAttributeMetadataJsonAsync"/>'s
/// type-cast GET URL tells Dataverse how to *read* the response, but that
/// context never comes back as part of the response body itself — so a
/// clone of it, PUT back with no <c>@odata.type</c> of its own, leaves
/// Dataverse to fall back to resolving the concrete subtype some other way,
/// which then rejects whichever of that type's own properties don't belong
/// on whatever it fell back to. Confirmed live on both Boolean (rejected
/// <c>DefaultValue</c>) and Picklist (rejected a handful of formula-column
/// properties, one at a time) — setting <c>@odata.type</c> explicitly,
/// matching <see cref="BuildCreateBody"/>'s own expression for it, resolved
/// both with no per-property stripping needed at all.
/// </summary>
public static class AttributeMetadataJsonBuilder
{
    /// <summary>
    /// Every attribute type this tool can update via the ordinary
    /// full-object PUT (<see cref="ApplyUpdateFields"/>) — a superset of
    /// <see cref="CreatableTypes"/>, since <c>Owner</c>/<c>Lookup</c>/
    /// <c>Customer</c>/<c>State</c>/<c>Status</c> already exist on every
    /// table (or come from the separate relationship-creation path — see
    /// <see cref="BuildRelationshipCreateBody"/>/<see cref="BuildCustomerRelationshipCreateBody"/>)
    /// and so are only ever updated, never created this way.
    /// Still excluded entirely: <c>Double</c> (no official Microsoft example
    /// of its create *or* update shape could be found — every type here is
    /// confirmed against one, and guessing at a shape that could corrupt a
    /// live table's schema isn't a risk worth taking), and anything else not
    /// investigated at all (<c>Uniqueidentifier</c>/<c>PartyList</c>/
    /// <c>File</c>/<c>Image</c>/<c>EntityName</c>/<c>ManagedProperty</c>).
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedTypes = new HashSet<string>
    {
        "String", "Memo", "Integer", "BigInt", "Decimal", "Money", "DateTime",
        "Boolean", "Picklist", "MultiSelectPicklist",
        "Owner", "Lookup", "Customer", "State", "Status",
    };

    /// <summary>
    /// Types this tool can create via a plain attribute POST
    /// (<see cref="BuildCreateBody"/>) — everything in <see cref="SupportedTypes"/>
    /// except the five that are either never independently creatable
    /// (<c>Owner</c>/<c>State</c>/<c>Status</c> already exist on every table)
    /// or created a structurally different way
    /// (<c>Lookup</c> via <see cref="BuildRelationshipCreateBody"/>,
    /// <c>Customer</c> via <see cref="BuildCustomerRelationshipCreateBody"/>).
    /// </summary>
    public static readonly IReadOnlySet<string> CreatableTypes = new HashSet<string>
    {
        "String", "Memo", "Integer", "BigInt", "Decimal", "Money", "DateTime",
        "Boolean", "Picklist", "MultiSelectPicklist",
    };

    /// <summary>
    /// Builds a brand-new attribute's create body from this tool's own
    /// curated <see cref="AttributeDefinition"/> — only ever called for an
    /// attribute that doesn't exist live yet, and only for a type in
    /// <see cref="CreatableTypes"/>.
    /// </summary>
    /// <param name="attribute">The column to create.</param>
    /// <param name="globalOptionSetMetadataId">
    /// Required (non-null) when <paramref name="attribute"/> is a Picklist/
    /// MultiSelectPicklist with a <see cref="AttributeDefinition.GlobalOptionSetName"/>
    /// — the target global choice's own <c>MetadataId</c>, resolved live by
    /// the caller (<see cref="Conversion.TableImportService"/>, via
    /// <see cref="Dataverse.IDataverseClient.TryGetGlobalOptionSetJsonAsync"/>)
    /// before this method ever runs. Confirmed live that the attribute-create
    /// endpoint's <c>GlobalOptionSet@odata.bind</c> only accepts a raw
    /// MetadataId GUID here — the <c>Name=</c> alternate-key form that works
    /// for other bind targets 500s with "Guid should contain 32 digits with
    /// 4 dashes" for this one. Ignored for every other type/case.
    /// </param>
    /// <exception cref="InvalidOperationException"><paramref name="attribute"/> has no <see cref="AttributeDefinition.SchemaName"/> — required to create a column, and never inferred.</exception>
    /// <exception cref="NotSupportedException"><paramref name="attribute"/>'s own <see cref="AttributeDefinition.Type"/> isn't in <see cref="CreatableTypes"/>.</exception>
    public static JsonObject BuildCreateBody(AttributeDefinition attribute, Guid? globalOptionSetMetadataId = null)
    {
        if (attribute.SchemaName is null)
        {
            throw new InvalidOperationException($"'{attribute.Name}' has no SchemaName in the local YAML — required to create a column, and this tool never guesses one.");
        }

        var body = new JsonObject
        {
            ["@odata.type"] = $"Microsoft.Dynamics.CRM.{attribute.Type}AttributeMetadata",
            ["AttributeType"] = attribute.Type,
            ["AttributeTypeName"] = new JsonObject { ["Value"] = $"{attribute.Type}Type" },
            ["SchemaName"] = attribute.SchemaName,
            // "None" (Dataverse's own default) is this tool's own omitted
            // case (see DefaultValueConventions.RequiredLevelOrNull) — made
            // explicit here since a create body needs *some* value, and
            // "None" is exactly what leaving it unset already means.
            ["RequiredLevel"] = new JsonObject { ["Value"] = attribute.RequiredLevel ?? "None", ["CanBeChanged"] = true },
        };

        if (attribute.DisplayName is not null)
        {
            body["DisplayName"] = DataverseLabelJson.Build(attribute.DisplayName);
        }

        if (attribute.Description is not null)
        {
            body["Description"] = DataverseLabelJson.Build(attribute.Description);
        }

        switch (attribute.Type)
        {
            case "String":
                body["MaxLength"] = attribute.MaxLength ?? 100;
                body["FormatName"] = new JsonObject { ["Value"] = attribute.Format ?? "Text" };
                break;

            case "Memo":
                body["MaxLength"] = attribute.MaxLength ?? 2000;
                body["Format"] = "TextArea";
                break;

            case "Integer":
                body["MinValue"] = (int)(attribute.MinValue ?? int.MinValue);
                body["MaxValue"] = (int)(attribute.MaxValue ?? int.MaxValue);
                body["Format"] = attribute.Format ?? "None";
                break;

            case "BigInt":
                // No MinValue/MaxValue in Microsoft's own documented create
                // example for BigInt — confirmed, not an oversight.
                break;

            case "Decimal":
                body["MinValue"] = attribute.MinValue ?? -100000000000.0;
                body["MaxValue"] = attribute.MaxValue ?? 100000000000.0;
                body["Precision"] = attribute.Precision ?? 2;
                break;

            case "Money":
                // PrecisionSource 1 = organization setting — the ordinary
                // case when nothing more specific was captured.
                body["PrecisionSource"] = attribute.PrecisionSource ?? 1;
                break;

            case "DateTime":
                body["Format"] = attribute.Format ?? "DateAndTime";
                break;

            case "Boolean":
                body["DefaultValue"] = attribute.DefaultValue ?? false;
                body["OptionSet"] = new JsonObject
                {
                    ["TrueOption"] = new JsonObject { ["Value"] = 1, ["Label"] = DataverseLabelJson.Build(attribute.TrueOptionLabel ?? "True") },
                    ["FalseOption"] = new JsonObject { ["Value"] = 0, ["Label"] = DataverseLabelJson.Build(attribute.FalseOptionLabel ?? "False") },
                    ["OptionSetType"] = "Boolean",
                };
                break;

            case "Picklist":
            case "MultiSelectPicklist":
                if (attribute.Type == "MultiSelectPicklist")
                {
                    // Confirmed against Microsoft's own documented example:
                    // unlike every other type here, AttributeType itself is
                    // the literal string "Virtual", not "MultiSelectPicklist"
                    // — @odata.type and AttributeTypeName.Value (set
                    // generically above) are unaffected.
                    body["AttributeType"] = "Virtual";
                }

                body["SourceTypeMask"] = 0;

                if (attribute.GlobalOptionSetName is not null)
                {
                    // Confirmed live: unlike other bind targets, the
                    // attribute-create endpoint rejects the Name= alternate
                    // key here — "Guid should contain 32 digits with 4
                    // dashes" — even with the leading '/' present or absent.
                    // Only a raw MetadataId GUID works, so the caller
                    // (TableImportService) resolves it live first via
                    // IDataverseClient.TryGetGlobalOptionSetJsonAsync and
                    // passes it through here.
                    if (globalOptionSetMetadataId is null)
                    {
                        throw new InvalidOperationException($"'{attribute.Name}' targets the global choice '{attribute.GlobalOptionSetName}', but its MetadataId was never resolved before building the create body.");
                    }

                    body["GlobalOptionSet@odata.bind"] = $"/GlobalOptionSetDefinitions({globalOptionSetMetadataId.Value:D})";
                }
                else
                {
                    body["OptionSet"] = new JsonObject
                    {
                        ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadata",
                        ["Options"] = BuildOptionsArray(attribute.Options!),
                        ["IsGlobal"] = false,
                        ["OptionSetType"] = "Picklist",
                    };
                }

                break;

            default:
                throw new NotSupportedException($"'{attribute.Type}' isn't one of the attribute types this tool can create yet — see {nameof(AttributeMetadataJsonBuilder)}.{nameof(CreatableTypes)}.");
        }

        return body;
    }

    /// <summary>
    /// Mutates <paramref name="existing"/> (the attribute's full, live JSON
    /// representation, fetched immediately beforehand via the type-cast GET
    /// so nothing this tool doesn't understand gets lost) in place, setting
    /// only the fields <paramref name="attribute"/> actually specifies. A
    /// null field on <paramref name="attribute"/> means "don't touch this",
    /// same as everywhere else in this tool — it's never treated as "reset
    /// to some default", since <paramref name="existing"/> already holds
    /// Dataverse's own current value there.
    ///
    /// Sets <c>@odata.type</c> explicitly, first — see this class's own
    /// top-level doc comment for why <paramref name="existing"/> doesn't
    /// already carry one despite coming from a type-cast GET, and what
    /// Dataverse rejects if it's left unset. Confirmed live for Boolean and
    /// Picklist specifically (both previously failed a plain-attribute
    /// update with no <c>@odata.type</c>, rejecting a different property
    /// each time, and both now succeed); not independently re-verified for
    /// every other type in <see cref="SupportedTypes"/>, but there's no
    /// reason to expect this more explicit body would regress any of them —
    /// if anything the reverse, since they were only ever working via
    /// whatever Dataverse happened to infer without it.
    /// </summary>
    /// <exception cref="NotSupportedException"><paramref name="attribute"/>'s own <see cref="AttributeDefinition.Type"/> isn't in <see cref="SupportedTypes"/>.</exception>
    public static void ApplyUpdateFields(JsonObject existing, AttributeDefinition attribute)
    {
        existing["@odata.type"] = $"Microsoft.Dynamics.CRM.{attribute.Type}AttributeMetadata";

        if (attribute.DisplayName is not null)
        {
            existing["DisplayName"] = DataverseLabelJson.Build(attribute.DisplayName);
        }

        if (attribute.Description is not null)
        {
            existing["Description"] = DataverseLabelJson.Build(attribute.Description);
        }

        if (attribute.RequiredLevel is not null)
        {
            existing["RequiredLevel"] = new JsonObject { ["Value"] = attribute.RequiredLevel, ["CanBeChanged"] = true };
        }

        switch (attribute.Type)
        {
            case "String":
                if (attribute.MaxLength is not null)
                {
                    existing["MaxLength"] = attribute.MaxLength.Value;
                }

                if (attribute.Format is not null)
                {
                    existing["FormatName"] = new JsonObject { ["Value"] = attribute.Format };
                }

                break;

            case "Memo":
                if (attribute.MaxLength is not null)
                {
                    existing["MaxLength"] = attribute.MaxLength.Value;
                }

                break;

            case "Integer":
                if (attribute.MinValue is not null)
                {
                    existing["MinValue"] = (int)attribute.MinValue.Value;
                }

                if (attribute.MaxValue is not null)
                {
                    existing["MaxValue"] = (int)attribute.MaxValue.Value;
                }

                break;

            case "BigInt":
                break;

            case "Decimal":
                if (attribute.MinValue is not null)
                {
                    existing["MinValue"] = attribute.MinValue.Value;
                }

                if (attribute.MaxValue is not null)
                {
                    existing["MaxValue"] = attribute.MaxValue.Value;
                }

                if (attribute.Precision is not null)
                {
                    existing["Precision"] = attribute.Precision.Value;
                }

                break;

            case "Money":
                if (attribute.PrecisionSource is not null)
                {
                    existing["PrecisionSource"] = attribute.PrecisionSource.Value;
                }

                if (attribute.Precision is not null)
                {
                    existing["Precision"] = attribute.Precision.Value;
                }

                if (attribute.MinValue is not null)
                {
                    existing["MinValue"] = attribute.MinValue.Value;
                }

                if (attribute.MaxValue is not null)
                {
                    existing["MaxValue"] = attribute.MaxValue.Value;
                }

                break;

            case "DateTime":
                if (attribute.Format is not null)
                {
                    existing["Format"] = attribute.Format;
                }

                break;

            case "Boolean":
                if (attribute.DefaultValue is not null)
                {
                    existing["DefaultValue"] = attribute.DefaultValue.Value;
                }

                break;

            // Options themselves are never set here — see this class's own
            // doc comment and BuildOptionChangePlans. (An earlier version of
            // this case stripped a handful of formula-column-related
            // properties that Dataverse was rejecting on PUT — that was
            // treating a symptom of the missing @odata.type above, not the
            // actual cause; no longer needed once @odata.type is set
            // explicitly.)
            case "Picklist":
            case "MultiSelectPicklist":
                break;

            // These five never have their own type-specific fields touched
            // by an update — Owner/Lookup/Customer's own defining
            // properties (Targets etc.) are immutable after creation
            // (checked in TableImportService before this is ever called),
            // and State/Status's options go through BuildOptionChangePlans
            // instead, exactly like Picklist/MultiSelectPicklist above.
            case "Owner":
            case "Lookup":
            case "Customer":
            case "State":
            case "Status":
                break;

            default:
                throw new NotSupportedException($"'{attribute.Type}' isn't one of the attribute types this tool can update yet — see {nameof(AttributeMetadataJsonBuilder)}.{nameof(SupportedTypes)}.");
        }
    }

    /// <summary>
    /// Builds the <c>RelationshipDefinitions</c> POST body that creates a
    /// brand-new, single-target Lookup column — see
    /// <see cref="Dataverse.IDataverseClient.CreateOneToManyRelationshipAsync"/>.
    /// Confirmed against Microsoft's own documented example for the request
    /// shape itself; <c>CascadeConfiguration</c> below defaults to the Maker
    /// UI's own default relationship behavior for a brand-new lookup —
    /// "Referential" (see <see cref="RelationshipBehaviors"/>) — rather than
    /// "Parental" (all-Cascade): an entity can only be the child in one
    /// Parental relationship at a time, so defaulting new lookups to
    /// Parental broke creating a second lookup to a different parent entity
    /// once one Parental relationship already existed (confirmed live:
    /// Dataverse error 0x80047007, "is parented to Entity ... Cannot create
    /// another parental relation"). <see cref="AttributeDefinition.RelationshipBehavior"/>
    /// overrides that default when a maker deliberately wants Parental (or
    /// Referential, Restrict Delete) instead.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="attribute"/> is missing <see cref="AttributeDefinition.SchemaName"/>, <see cref="AttributeDefinition.RelationshipSchemaName"/>, or a single <see cref="AttributeDefinition.Targets"/> entry, or has a <see cref="AttributeDefinition.RelationshipBehavior"/> that isn't one of <see cref="RelationshipBehaviors.Names"/> — <see cref="AttributeChangeValidator.ValidateCreate"/> should already have caught this first.</exception>
    public static JsonObject BuildRelationshipCreateBody(string entityLogicalName, AttributeDefinition attribute)
    {
        if (attribute.SchemaName is null)
        {
            throw new InvalidOperationException($"'{attribute.Name}' has no SchemaName in the local YAML — required to create a column, and this tool never guesses one.");
        }

        if (attribute.RelationshipSchemaName is null)
        {
            throw new InvalidOperationException($"'{attribute.Name}' has no RelationshipSchemaName in the local YAML — required to create a Lookup column.");
        }

        if (attribute.Targets is not { Count: 1 })
        {
            throw new InvalidOperationException($"'{attribute.Name}' must have exactly one Targets entry to create a plain Lookup column.");
        }

        var cascadeConfiguration = RelationshipBehaviors.CascadeConfigurationOrNull(attribute.RelationshipBehavior ?? RelationshipBehaviors.Referential)
            ?? throw new InvalidOperationException($"'{attribute.RelationshipBehavior}' isn't a valid RelationshipBehavior for '{attribute.Name}' — expected one of: {string.Join(", ", RelationshipBehaviors.Names)}.");

        var target = attribute.Targets[0];

        var lookup = new JsonObject
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
            ["AttributeType"] = "Lookup",
            ["AttributeTypeName"] = new JsonObject { ["Value"] = "LookupType" },
            ["SchemaName"] = attribute.SchemaName,
            ["RequiredLevel"] = new JsonObject { ["Value"] = attribute.RequiredLevel ?? "None", ["CanBeChanged"] = true },
        };

        if (attribute.DisplayName is not null)
        {
            lookup["DisplayName"] = DataverseLabelJson.Build(attribute.DisplayName);
        }

        if (attribute.Description is not null)
        {
            lookup["Description"] = DataverseLabelJson.Build(attribute.Description);
        }

        return new JsonObject
        {
            ["SchemaName"] = attribute.RelationshipSchemaName,
            ["@odata.type"] = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata",
            ["AssociatedMenuConfiguration"] = new JsonObject
            {
                ["Behavior"] = "UseCollectionName",
                ["Group"] = "Details",
                ["Label"] = DataverseLabelJson.Build(attribute.DisplayName ?? attribute.Name),
                ["Order"] = 10000,
            },
            ["CascadeConfiguration"] = cascadeConfiguration,
            // A target's primary key is always its own logical name + "id"
            // — Dataverse's own fixed, universal naming convention (e.g.
            // accountid, contactid), safe to derive rather than ask for.
            ["ReferencedAttribute"] = $"{target}id",
            ["ReferencedEntity"] = target,
            ["ReferencingEntity"] = entityLogicalName,
            ["Lookup"] = lookup,
        };
    }

    /// <summary>
    /// Builds the <c>CreateCustomerRelationships</c> action body — see
    /// <see cref="Dataverse.IDataverseClient.CreateCustomerRelationshipsAsync"/>.
    /// Both relationship SchemaNames are derived from the attribute's own
    /// <see cref="AttributeDefinition.SchemaName"/> plus the fixed
    /// account/contact targets — Customer's shape never varies, so there's
    /// nothing here worth its own YAML field for (see
    /// `docs/yaml-conventions.md`).
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="attribute"/> has no <see cref="AttributeDefinition.SchemaName"/> — <see cref="AttributeChangeValidator.ValidateCreate"/> should already have caught this first.</exception>
    public static JsonObject BuildCustomerRelationshipCreateBody(string entityLogicalName, AttributeDefinition attribute)
    {
        if (attribute.SchemaName is null)
        {
            throw new InvalidOperationException($"'{attribute.Name}' has no SchemaName in the local YAML — required to create a column, and this tool never guesses one.");
        }

        // Confirmed against Microsoft's own documented example: unlike a
        // plain Lookup's own nested attribute, Customer's carries no
        // RequiredLevel of its own.
        var lookup = new JsonObject
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.ComplexLookupAttributeMetadata",
            ["AttributeType"] = "Lookup",
            ["AttributeTypeName"] = new JsonObject { ["Value"] = "LookupType" },
            ["SchemaName"] = attribute.SchemaName,
        };

        if (attribute.DisplayName is not null)
        {
            lookup["DisplayName"] = DataverseLabelJson.Build(attribute.DisplayName);
        }

        if (attribute.Description is not null)
        {
            lookup["Description"] = DataverseLabelJson.Build(attribute.Description);
        }

        return new JsonObject
        {
            ["OneToManyRelationships"] = new JsonArray(
                new JsonObject { ["SchemaName"] = $"{attribute.SchemaName}_account", ["ReferencedEntity"] = "account", ["ReferencingEntity"] = entityLogicalName },
                new JsonObject { ["SchemaName"] = $"{attribute.SchemaName}_contact", ["ReferencedEntity"] = "contact", ["ReferencingEntity"] = entityLogicalName }),
            ["Lookup"] = lookup,
        };
    }

    /// <summary>
    /// Diffs an option-bearing column's local vs. existing choice values
    /// into the action-based requests needed to reconcile them — never part
    /// of the ordinary attribute PUT (<see cref="ApplyUpdateFields"/>).
    /// Per-type behaviour (see `docs/yaml-conventions.md` for the full
    /// rationale):
    /// <list type="bullet">
    /// <item>Boolean: only <c>TrueOptionLabel</c>/<c>FalseOptionLabel</c>
    /// renames (fixed at Value 1/0).</item>
    /// <item>Picklist/MultiSelectPicklist (local option set only — a
    /// <see cref="AttributeDefinition.GlobalOptionSetName"/> column never
    /// gets automatic option changes, see this class's own top-level doc
    /// comment on scope): unmatched local values insert, matched values with
    /// a different label rename, and — only when the value-set is otherwise
    /// identical and just the order differs — one reorder request. An
    /// existing value missing from the local YAML is never deleted
    /// automatically, mirroring <see cref="Conversion.AttributeImportAction.WouldRemove"/>'s
    /// same policy for a whole column.</item>
    /// <item>State: only when the live column turns out to use a *local*
    /// option set (see the next paragraph) — a rename of an already-live
    /// value's label (<c>UpdateStateValue</c>). Nothing is ever inserted —
    /// a table's state model is fixed at creation, and there's no
    /// documented <c>InsertStateValue</c> action to do it with anyway.</item>
    /// <item>Status: matched by <see cref="AttributeOptionDefinition.Value"/>
    /// first, same as Picklist — a matched value with a different label
    /// renames (<c>UpdateOptionValue</c>, the same local action Picklist
    /// uses; Status's own OptionSet is ordinary in that respect). An
    /// unmatched local value falls back to a *label* match against what's
    /// live before treating it as new — confirmed against Microsoft's own
    /// docs that <c>InsertStatusValue</c>'s request has no <c>Value</c> of
    /// its own to match by (Dataverse always assigns one), so a status this
    /// tool already inserted on a previous run, whose YAML hasn't been
    /// re-exported to pick up the real assigned value yet, is recognized by
    /// label instead of being inserted a second time. Only once *neither*
    /// matches is it a genuine insert (<c>InsertStatusValue</c>) — and only
    /// when <see cref="AttributeOptionDefinition.State"/> is set, since
    /// Dataverse needs to know which State the new status belongs to and
    /// this tool never guesses it; missing State surfaces as a warning
    /// instead (see <see cref="AttributeChangeValidator.Warnings"/>).</item>
    /// </list>
    /// Confirmed live, not assumed: on a real tenant, both <c>statecode</c>
    /// and <c>statuscode</c> come back backed by a *global* option set
    /// (<c>{entity}_statecode</c>/<c>{entity}_statuscode</c>) on every table
    /// checked, standard and custom alike — not the "local, per-column"
    /// shape this class otherwise assumes for State/Status. Since this class
    /// never touches a global option set's own options (see the "Global
    /// option sets" scope decision), <see cref="BuildRenameOnlyOptionChangePlans"/>
    /// only ever runs when <c>existing.GlobalOptionSetName</c> is null —
    /// which in practice makes State/Status option renaming inert on most
    /// real tables, by design rather than by omission.
    /// </summary>
    public static IReadOnlyList<OptionChangePlan> BuildOptionChangePlans(string entityLogicalName, AttributeDefinition local, AttributeDefinition existing)
    {
        return local.Type switch
        {
            "Boolean" => BuildBooleanOptionChangePlans(entityLogicalName, local, existing),
            "State" when existing.GlobalOptionSetName is null => BuildRenameOnlyOptionChangePlans(entityLogicalName, local, existing, OptionChangeAction.UpdateStateValue),
            "Status" when existing.GlobalOptionSetName is null => BuildStatusOptionChangePlans(entityLogicalName, local, existing),
            "Picklist" or "MultiSelectPicklist" when local.Options is not null && local.GlobalOptionSetName is null && existing.GlobalOptionSetName is null => BuildLocalOptionSetChangePlans(entityLogicalName, local, existing),
            _ => [],
        };
    }

    private static IReadOnlyList<OptionChangePlan> BuildBooleanOptionChangePlans(string entityLogicalName, AttributeDefinition local, AttributeDefinition existing)
    {
        var plans = new List<OptionChangePlan>();
        var existingTrueLabel = existing.TrueOptionLabel ?? "True";
        var existingFalseLabel = existing.FalseOptionLabel ?? "False";

        if (local.TrueOptionLabel is not null && local.TrueOptionLabel != existingTrueLabel)
        {
            plans.Add(BuildUpdateOptionPlan(entityLogicalName, local.Name, 1, local.TrueOptionLabel));
        }

        if (local.FalseOptionLabel is not null && local.FalseOptionLabel != existingFalseLabel)
        {
            plans.Add(BuildUpdateOptionPlan(entityLogicalName, local.Name, 0, local.FalseOptionLabel));
        }

        return plans;
    }

    private static IReadOnlyList<OptionChangePlan> BuildRenameOnlyOptionChangePlans(string entityLogicalName, AttributeDefinition local, AttributeDefinition existing, OptionChangeAction renameAction)
    {
        if (local.Options is null)
        {
            return [];
        }

        var existingByValue = (existing.Options ?? []).ToDictionary(o => o.Value);
        var plans = new List<OptionChangePlan>();

        foreach (var option in local.Options)
        {
            if (existingByValue.TryGetValue(option.Value, out var existingOption) && existingOption.Label != option.Label)
            {
                var body = new JsonObject
                {
                    ["AttributeLogicalName"] = local.Name,
                    ["EntityLogicalName"] = entityLogicalName,
                    ["Value"] = option.Value,
                    ["Label"] = DataverseLabelJson.Build(option.Label),
                    ["MergeLabels"] = true,
                };
                plans.Add(new OptionChangePlan(renameAction, body, $"rename option {option.Value} to '{option.Label}'"));
            }

            // A local value with no live match is never inserted here — see
            // this method's own doc comment on AttributeChangeValidator.Warnings.
        }

        return plans;
    }

    /// <summary>
    /// Status's own diff — deliberately not <see cref="OptionSetDiffer"/>
    /// (which matches purely by Value): a value-only match would either
    /// never detect a genuine insert (Status never lets this tool choose a
    /// Value) or, worse, insert the same status reason twice across two
    /// runs before the YAML is re-exported to pick up the real assigned
    /// Value. See this class's own <see cref="BuildOptionChangePlans"/> doc
    /// comment for the full match-by-Value-then-Label reasoning.
    /// </summary>
    private static IReadOnlyList<OptionChangePlan> BuildStatusOptionChangePlans(string entityLogicalName, AttributeDefinition local, AttributeDefinition existing)
    {
        if (local.Options is null)
        {
            return [];
        }

        var existingByValue = (existing.Options ?? []).ToDictionary(o => o.Value);
        var existingLabels = (existing.Options ?? []).Select(o => o.Label).ToHashSet(StringComparer.Ordinal);
        var plans = new List<OptionChangePlan>();

        foreach (var option in local.Options)
        {
            if (existingByValue.TryGetValue(option.Value, out var existingOption))
            {
                if (existingOption.Label != option.Label)
                {
                    plans.Add(BuildUpdateOptionPlan(entityLogicalName, local.Name, option.Value, option.Label));
                }

                continue;
            }

            if (existingLabels.Contains(option.Label))
            {
                // No Value match, but the label already exists live — most
                // likely a status this tool inserted on a previous run,
                // with the YAML not yet re-exported to pick up Dataverse's
                // own assigned Value. Never a duplicate insert.
                continue;
            }

            if (option.State is not null)
            {
                plans.Add(BuildInsertStatusPlan(entityLogicalName, local.Name, option));
            }

            // No live match by Value or Label, and no State given: never
            // inserted — see AttributeChangeValidator.Warnings for the
            // surfaced note instead of a guess.
        }

        return plans;
    }

    private static OptionChangePlan BuildInsertStatusPlan(string entityLogicalName, string attributeLogicalName, AttributeOptionDefinition option) =>
        new(OptionChangeAction.InsertStatusValue, new JsonObject
        {
            ["AttributeLogicalName"] = attributeLogicalName,
            ["EntityLogicalName"] = entityLogicalName,
            ["Label"] = DataverseLabelJson.Build(option.Label),
            ["StateCode"] = option.State!.Value,
        }, $"insert status '{option.Label}' (state {option.State})");

    private static IReadOnlyList<OptionChangePlan> BuildLocalOptionSetChangePlans(string entityLogicalName, AttributeDefinition local, AttributeDefinition existing) =>
        OptionSetDiffer.Diff(local.Options!, existing.Options,
            option => BuildInsertOptionPlan(entityLogicalName, local.Name, option),
            (value, label) => BuildUpdateOptionPlan(entityLogicalName, local.Name, value, label),
            values => BuildOrderOptionsPlan(entityLogicalName, local.Name, values));

    private static OptionChangePlan BuildInsertOptionPlan(string entityLogicalName, string attributeLogicalName, AttributeOptionDefinition option) =>
        new(OptionChangeAction.InsertOption, new JsonObject
        {
            ["AttributeLogicalName"] = attributeLogicalName,
            ["EntityLogicalName"] = entityLogicalName,
            ["Value"] = option.Value,
            ["Label"] = DataverseLabelJson.Build(option.Label),
        }, $"insert option '{option.Label}' ({option.Value})");

    private static OptionChangePlan BuildUpdateOptionPlan(string entityLogicalName, string attributeLogicalName, int value, string label) =>
        new(OptionChangeAction.UpdateOption, new JsonObject
        {
            ["AttributeLogicalName"] = attributeLogicalName,
            ["EntityLogicalName"] = entityLogicalName,
            ["Value"] = value,
            ["Label"] = DataverseLabelJson.Build(label),
            ["MergeLabels"] = true,
        }, $"rename option {value} to '{label}'");

    private static OptionChangePlan BuildOrderOptionsPlan(string entityLogicalName, string attributeLogicalName, IReadOnlyList<int> values) =>
        new(OptionChangeAction.OrderOptions, new JsonObject
        {
            ["EntityLogicalName"] = entityLogicalName,
            ["AttributeLogicalName"] = attributeLogicalName,
            ["Values"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray()),
        }, "reorder options");

    private static JsonArray BuildOptionsArray(IReadOnlyList<AttributeOptionDefinition> options) =>
        new(options.Select(o => (JsonNode)new JsonObject { ["Value"] = o.Value, ["Label"] = DataverseLabelJson.Build(o.Label) }).ToArray());
}

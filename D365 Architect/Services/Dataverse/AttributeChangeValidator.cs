using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Catches the common ways a column create/update would fail (or worse,
/// silently corrupt something) before <c>table import</c> ever sends a
/// request — confirmed against Microsoft's own documented constraints
/// where one exists (immutable <c>Type</c>/<c>SchemaName</c>, Integer's
/// <c>MinValue</c>/<c>MaxValue</c> range, Decimal's <c>Precision</c> range),
/// against a reasonable same-platform extension where the exact number
/// wasn't independently re-confirmed (Money's <c>Precision</c> range, by
/// analogy to Decimal's identically-shaped property), or against this
/// tool's own already-verified conventions (a custom column's SchemaName
/// always carrying a publisher prefix) where no Microsoft page gives an
/// exact bound at all. <see cref="AttributeMetadataJsonBuilder"/> builds
/// the request bodies; this decides whether one should be built at all —
/// in particular it's what keeps <see cref="AttributeMetadataJsonBuilder"/>'s
/// own <c>(int)</c> casts of Integer's <c>MinValue</c>/<c>MaxValue</c> safe,
/// by refusing anything outside <see cref="int"/>'s range before either
/// create or update ever reaches that cast.
/// </summary>
public static class AttributeChangeValidator
{
    /// <summary>Dataverse's own documented RequiredLevel values (the managed property's <c>Value</c>) — anything else would be rejected.</summary>
    private static readonly IReadOnlySet<string> ValidRequiredLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "None", "Recommended", "ApplicationRequired", "SystemRequired",
    };

    /// <summary>
    /// <c>IntegerAttributeMetadata.MinValue</c>/<c>MaxValue</c>'s documented
    /// range — confirmed directly against Microsoft Learn ("Possible values
    /// are -2147483648 to 2147483647"), not assumed from <see cref="int"/>'s
    /// own range coinciding with it.
    /// </summary>
    private const long IntegerMinAllowed = -2147483648L;
    private const long IntegerMaxAllowed = 2147483647L;

    /// <summary>
    /// <c>DecimalAttributeMetadata.Precision</c>'s documented range —
    /// confirmed directly against Microsoft Learn ("Possible values are
    /// 1-10"). Applied to Money's <c>Precision</c> too: Money's own page
    /// only confirms the same default (2), not this exact bound, but it's
    /// the identical property shape on the same platform and there's no
    /// indication it differs — flagged here, not silently treated as
    /// equally confirmed.
    /// </summary>
    private const decimal MinPrecision = 1m;
    private const decimal MaxPrecision = 10m;

    /// <summary>
    /// Corroborated across multiple sources at 4000, but no single
    /// canonical Microsoft Learn page states it as an explicit numeric
    /// ceiling the way Integer's range or Decimal's Precision range do —
    /// kept as a validation anyway since every source agrees and Dataverse
    /// would reject anything higher regardless, but called out here (and in
    /// `docs/yaml-conventions.md`) as the one bound in this file that isn't
    /// a direct citation.
    /// </summary>
    private const int MaxStringLength = 4000;

    /// <returns>Why creating <paramref name="local"/> would fail, or null when it looks safe to attempt.</returns>
    public static string? ValidateCreate(AttributeDefinition local)
    {
        if (local.SchemaName is null)
        {
            return $"'{local.Name}' has no SchemaName in the local YAML — required to create a column, and this tool never guesses one.";
        }

        if (!DataverseSchemaNaming.SchemaNamePattern.IsMatch(local.SchemaName))
        {
            var example = local.SchemaName.Contains('_') ? "letters/digits/underscores only, e.g. 'new_BankName'" : $"a customization prefix, e.g. 'new_{local.SchemaName}'";
            return $"SchemaName '{local.SchemaName}' isn't valid — expected {example}, and this tool never invents or corrects one.";
        }

        // Dataverse derives the new attribute's LogicalName by lowercasing
        // SchemaName at create time — it isn't something you can set
        // yourself. If the local YAML's Name doesn't already match that,
        // the column that actually gets created will have a different
        // logical name than the YAML claims, and every later import would
        // treat it as a brand-new column instead of recognizing it.
        var derivedLogicalName = local.SchemaName.ToLowerInvariant();
        if (!string.Equals(local.Name, derivedLogicalName, StringComparison.Ordinal))
        {
            return $"Name '{local.Name}' won't match the logical name Dataverse actually creates — it derives that from SchemaName by lowercasing it ('{derivedLogicalName}'), never from Name directly. Set Name to '{derivedLogicalName}' (or change SchemaName to match).";
        }

        if (local.Type is "Picklist" or "MultiSelectPicklist")
        {
            var optionsError = ValidateOptionsForCreate(local);
            if (optionsError is not null)
            {
                return optionsError;
            }
        }

        if (local.Type == "Lookup")
        {
            var lookupError = ValidateLookupForCreate(local);
            if (lookupError is not null)
            {
                return lookupError;
            }
        }

        if (local.Type == "Customer")
        {
            var customerError = ValidateCustomerForCreate(local);
            if (customerError is not null)
            {
                return customerError;
            }
        }

        return ValidateCommon(local, existing: null);
    }

    /// <summary>Picklist/MultiSelectPicklist create: either Options or GlobalOptionSetName, never both, and Options (when used) non-empty with unique, explicit Values — see AttributeOptionDefinition's own doc comment for why a value is never invented.</summary>
    private static string? ValidateOptionsForCreate(AttributeDefinition local)
    {
        if (local.Options is not null && local.GlobalOptionSetName is not null)
        {
            return $"'{local.Name}' specifies both Options and GlobalOptionSetName — a column uses one or the other, never both.";
        }

        if (local.GlobalOptionSetName is not null)
        {
            return null;
        }

        if (local.Options is null || local.Options.Count == 0)
        {
            return $"'{local.Name}' has no Options (or GlobalOptionSetName) in the local YAML — required to create a {local.Type} column, and this tool never invents choice values.";
        }

        var duplicateValues = local.Options.GroupBy(o => o.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateValues.Count > 0)
        {
            return $"'{local.Name}' has duplicate option Value(s): {string.Join(", ", duplicateValues)}.";
        }

        return null;
    }

    /// <summary>A plain (non-Customer) Lookup's create needs its own relationship SchemaName and exactly one target — see BuildRelationshipCreateBody's own doc comment on why more than one is out of scope.</summary>
    private static string? ValidateLookupForCreate(AttributeDefinition local)
    {
        if (local.RelationshipSchemaName is null)
        {
            return $"'{local.Name}' has no RelationshipSchemaName in the local YAML — required to create a Lookup column, and this tool never invents one.";
        }

        if (!DataverseSchemaNaming.SchemaNamePattern.IsMatch(local.RelationshipSchemaName))
        {
            return $"RelationshipSchemaName '{local.RelationshipSchemaName}' isn't valid — expected a customization prefix and letters/digits/underscores only, e.g. 'new_contact_new_bankaccount'.";
        }

        if (local.Targets is not { Count: 1 })
        {
            return $"'{local.Name}' must have exactly one Targets entry to create a plain Lookup column — more than one is a multi-table lookup, which this tool doesn't support creating yet.";
        }

        return null;
    }

    /// <summary>A Customer column's Targets are fixed by Dataverse itself — never anything this tool lets a maker choose.</summary>
    private static string? ValidateCustomerForCreate(AttributeDefinition local)
    {
        var targets = local.Targets is null ? [] : local.Targets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!targets.SetEquals(new[] { "account", "contact" }))
        {
            return $"'{local.Name}' is a Customer column but its Targets aren't exactly ['account', 'contact'] — that's fixed by Dataverse for every Customer column.";
        }

        return null;
    }

    /// <returns>Why updating <paramref name="local"/> onto <paramref name="existing"/> would fail, or null when it looks safe to attempt. Only checks fields this tool doesn't already guard elsewhere — see <see cref="Conversion.TableImportService"/>'s own Type/SchemaName-mismatch checks, which run before this and cover the two most common "that's not allowed" cases.</returns>
    public static string? ValidateUpdate(AttributeDefinition local, AttributeDefinition existing) => ValidateCommon(local, existing);

    private static string? ValidateCommon(AttributeDefinition local, AttributeDefinition? existing)
    {
        if (local.RequiredLevel is not null && !ValidRequiredLevels.Contains(local.RequiredLevel))
        {
            return $"'{local.RequiredLevel}' isn't a valid RequiredLevel — expected one of: {string.Join(", ", ValidRequiredLevels)}.";
        }

        // Confirmed live, not guessed: a BigInt column's attribute PUT
        // accepts the request (204, ModifiedOn left unchanged) but never
        // actually persists the change — DisplayName, Description, and
        // RequiredLevel were each tested individually and all three
        // silently no-op. Verified with a hand-built minimal body cloned
        // straight from the same GET this tool itself reads, entirely
        // bypassing this tool's own request construction, to rule out
        // anything on this side of the write. This looks like a genuine
        // Dataverse platform restriction specific to BigIntAttributeMetadata
        // (see `docs/yaml-conventions.md`'s BigInt note) — creating a BigInt
        // column is unaffected (see ValidateCreate, which never calls this
        // with existing set), only updating an already-live one always
        // silently does nothing, so it's refused up front rather than this
        // tool ever reporting "Imported." for a write that never took
        // effect.
        if (existing is not null && local.Type == "BigInt")
        {
            return "BigInt columns can't be updated after creation — confirmed live that Dataverse's attribute PUT accepts the request but never actually applies any change to a BigInt column (not DisplayName, Description, or RequiredLevel), even though it reports success. This tool refuses the update rather than claim a change that silently never took effect.";
        }

        if (local.Type is "String" or "Memo" && local.MaxLength is <= 0)
        {
            return $"MaxLength must be greater than 0 (was {local.MaxLength}).";
        }

        // Only enforced when MaxLength is actually the thing changing —
        // confirmed live against the real 'account' table that a column can
        // already carry a value this tool doesn't otherwise expect for its
        // type (see the Precision case below, found the same way); re-
        // sending an existing value unchanged, because some *other* field on
        // the same column is being updated, should never block on a value
        // nobody's actually trying to set.
        if (local.Type is "String" && local.MaxLength > MaxStringLength && (existing is null || local.MaxLength != existing.MaxLength))
        {
            return $"MaxLength {local.MaxLength} exceeds String's maximum of {MaxStringLength}.";
        }

        if (local.Type is "Integer")
        {
            if (local.MinValue is < IntegerMinAllowed or > IntegerMaxAllowed)
            {
                return $"MinValue {local.MinValue} is outside Integer's allowed range ({IntegerMinAllowed} to {IntegerMaxAllowed}).";
            }

            if (local.MaxValue is < IntegerMinAllowed or > IntegerMaxAllowed)
            {
                return $"MaxValue {local.MaxValue} is outside Integer's allowed range ({IntegerMinAllowed} to {IntegerMaxAllowed}).";
            }
        }

        if (local.Type is "Integer" or "Decimal" or "Money" && local.MinValue is not null && local.MaxValue is not null && local.MinValue > local.MaxValue)
        {
            return $"MinValue {local.MinValue} is greater than MaxValue {local.MaxValue}.";
        }

        // Same "only when actually changing" guard as MaxLength above —
        // confirmed live: the real 'account' table's own 'exchangerate'
        // column already sits at Precision 12, outside the 1-10 range
        // Decimal's own docs give for *setting* it. Whatever the reason
        // (grandfathered before the constraint existed, or a system column
        // never subject to the ordinary create-time check), blocking every
        // future update to that column over a value nobody's touching would
        // be exactly the kind of false positive this tool exists to avoid.
        if (local.Type is "Decimal" or "Money" && local.Precision is not null && (local.Precision < MinPrecision || local.Precision > MaxPrecision) && (existing is null || local.Precision != existing.Precision))
        {
            return $"Precision {local.Precision} is outside {local.Type}'s allowed range ({MinPrecision} to {MaxPrecision}).";
        }

        // Updating an existing Picklist/MultiSelectPicklist's own options
        // (AttributeMetadataJsonBuilder.BuildOptionChangePlans) only ever
        // covers insert/rename/reorder within the *same* option set —
        // rebinding the column to switch which set it uses (global-to-local,
        // local-to-global, or one global choice to another) isn't a
        // documented single-request operation this tool has confirmed, so
        // it's caught here rather than silently accepted as an "Update"
        // that would actually change nothing about the binding.
        if (existing is not null && local.Type is "Picklist" or "MultiSelectPicklist")
        {
            if (local.GlobalOptionSetName is not null && existing.GlobalOptionSetName is not null
                && !string.Equals(local.GlobalOptionSetName, existing.GlobalOptionSetName, StringComparison.OrdinalIgnoreCase))
            {
                return $"Can't change '{local.Name}' from the global choice '{existing.GlobalOptionSetName}' to '{local.GlobalOptionSetName}' — switching which global choice a column uses isn't supported by this tool yet.";
            }

            if (local.GlobalOptionSetName is not null && existing.GlobalOptionSetName is null)
            {
                return $"Can't change '{local.Name}' from a local choice to the global choice '{local.GlobalOptionSetName}' — switching isn't supported by this tool yet.";
            }

            if (local.Options is not null && existing.GlobalOptionSetName is not null)
            {
                return $"Can't change '{local.Name}' from the global choice '{existing.GlobalOptionSetName}' to a local choice — switching isn't supported by this tool yet.";
            }
        }

        return null;
    }

    /// <summary>
    /// Non-blocking cautions for an update that Dataverse itself is
    /// documented as *allowing* but warns against — e.g. lowering
    /// MaxLength/Precision below what existing data might already exceed.
    /// Unlike <see cref="ValidateUpdate"/>, these never stop the update
    /// from being planned; they're shown alongside it so a human can decide.
    /// </summary>
    public static IReadOnlyList<string> Warnings(AttributeDefinition local, AttributeDefinition existing)
    {
        var warnings = new List<string>();

        if (local.MaxLength is not null && existing.MaxLength is not null && local.MaxLength < existing.MaxLength)
        {
            warnings.Add($"Lowering MaxLength from {existing.MaxLength} to {local.MaxLength} — Dataverse allows this, but existing values longer than the new limit may cause errors or be truncated.");
        }

        if (local.Precision is not null && existing.Precision is not null && local.Precision < existing.Precision)
        {
            warnings.Add($"Lowering Precision from {existing.Precision} to {local.Precision} may affect existing data.");
        }

        // Only worth surfacing when the column is actually backed by a
        // local option set — confirmed live that State/Status normally use
        // a *global* one (account's own statecode/statuscode both do), and
        // this tool never touches a global option set's options at all (see
        // the "Global option sets" scope decision), so noting a would-be
        // insert there would be a misleading warning about something
        // genuinely out of scope rather than a real gap.
        if (local.Type is "State" or "Status" && local.Options is not null && existing.GlobalOptionSetName is null)
        {
            var existingValues = (existing.Options ?? []).Select(o => o.Value).ToHashSet();
            var existingLabels = (existing.Options ?? []).Select(o => o.Label).ToHashSet(StringComparer.Ordinal);

            // The Label fallback below is Status-specific only: it matches
            // AttributeMetadataJsonBuilder.BuildStatusOptionChangePlans' own
            // Value-then-Label matching, where a local option with no Value
            // match but a Label match isn't actually unmatched (most likely
            // a Status this tool already inserted, whose server-assigned
            // Value hasn't been re-exported into the YAML yet). A State
            // option's Value is never server-assigned or a placeholder — it's
            // a fixed, meaningful identity (0/1, etc.) — so a Label
            // coincidence never excuses a Value mismatch there; State is
            // warned on a Value-only mismatch instead.
            var unmatchedOptions = local.Type == "State"
                ? local.Options.Where(o => !existingValues.Contains(o.Value))
                : local.Options.Where(o => !existingValues.Contains(o.Value) && !existingLabels.Contains(o.Label));

            foreach (var option in unmatchedOptions)
            {
                warnings.Add(local.Type == "State"
                    ? $"Local option {option.Value} ('{option.Label}') has no live match — this tool never adds a new State value (a table's state model is fixed at creation), so it won't be applied."
                    : option.State is null
                        ? $"Local option ('{option.Label}') has no live match and no State given — this tool needs to know which State a new Status reason belongs to (Dataverse's own InsertStatusValue action requires it) and never guesses it, so it won't be applied. Add a `state` to this option."
                        : $"Local option ('{option.Label}') has no live match — will be inserted under State {option.State}. Dataverse assigns its own Value for a new Status reason; re-export after applying to pick up the real one (the placeholder Value {option.Value} in this YAML is never sent).");
            }
        }

        return warnings;
    }
}

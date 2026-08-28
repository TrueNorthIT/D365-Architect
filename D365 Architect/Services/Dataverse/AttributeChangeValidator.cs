using System.Text.RegularExpressions;
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
    /// A short alphanumeric prefix, an underscore, then the rest of the
    /// name using only letters/digits/underscores throughout — e.g.
    /// <c>new_BankName</c>, <c>cr7a3_Account_Rating</c>. This is the
    /// structural shape of every custom SchemaName confirmed live this
    /// session, not a guess at Dataverse's own exact validation regex or an
    /// attempt to check it against a specific registered publisher (that
    /// would need a live lookup this tool doesn't do) — it exists to catch
    /// the obvious mistakes (no prefix at all, or a space/dash/other
    /// character Dataverse's own schema name rules don't allow) before
    /// Dataverse does. Full-string match — unlike an earlier version of
    /// this pattern, nothing after the prefix is allowed to be an arbitrary
    /// character.
    /// </summary>
    private static readonly Regex SchemaNamePattern = new(@"^[A-Za-z][A-Za-z0-9]*_[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

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

        if (!SchemaNamePattern.IsMatch(local.SchemaName))
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

        return ValidateCommon(local, existing: null);
    }

    /// <returns>Why updating <paramref name="local"/> onto <paramref name="existing"/> would fail, or null when it looks safe to attempt. Only checks fields this tool doesn't already guard elsewhere — see <see cref="Conversion.TableImportService"/>'s own Type/SchemaName-mismatch checks, which run before this and cover the two most common "that's not allowed" cases.</returns>
    public static string? ValidateUpdate(AttributeDefinition local, AttributeDefinition existing) => ValidateCommon(local, existing);

    private static string? ValidateCommon(AttributeDefinition local, AttributeDefinition? existing)
    {
        if (local.RequiredLevel is not null && !ValidRequiredLevels.Contains(local.RequiredLevel))
        {
            return $"'{local.RequiredLevel}' isn't a valid RequiredLevel — expected one of: {string.Join(", ", ValidRequiredLevels)}.";
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

        return warnings;
    }
}

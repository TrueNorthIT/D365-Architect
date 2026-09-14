using System.Text.Json.Nodes;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// The relationship "Behavior" presets the Maker UI itself offers when
/// creating a new one-to-many relationship, each expanded to the
/// <c>CascadeConfiguration</c> Dataverse's Web API actually wants — see
/// <see cref="AttributeMetadataJsonBuilder.BuildRelationshipCreateBody"/>,
/// the only place this is used. "Configurable Cascading" (the Maker UI's
/// fourth option, letting each of the six behaviors be set independently)
/// is deliberately not included: nothing here needs a shape more granular
/// than these three named presets, and adding it would mean a nested YAML
/// field per behavior for a case nobody's asked for yet.
/// </summary>
public static class RelationshipBehaviors
{
    /// <summary>
    /// Referential — the Maker UI's own default for a brand-new lookup.
    /// This is what avoids <c>0x80047007</c> ("is parented to Entity ...
    /// Cannot create another parental relation") when the referencing
    /// entity already has a Parental relationship elsewhere, unlike this
    /// tool's original hardcoded all-Cascade body — but confirmed live
    /// (the hard way, on a real tenant) that an earlier version of this
    /// preset still hit that same error despite naming itself Referential:
    /// it had <c>Reparent: Cascade</c>, and Microsoft's own docs on what
    /// makes a relationship "parental" for this exact restriction (see
    /// `entity-relationship-behavior#BKMK_ParentalEntityRelationships`)
    /// say a relationship counts as Parental if *any* of Delete=Cascade,
    /// or Assign/Share/Unshare/Reparent ∈ {Cascade, UserOwned, Active} —
    /// Merge isn't a determinant at all. Every field below reflects that
    /// table, not this class's own earlier, wrong assumption.
    /// </summary>
    public const string Referential = "Referential";

    /// <summary>Referential, Restrict Delete — same as <see cref="Referential"/> except the parent record can't be deleted while children reference it.</summary>
    public const string ReferentialRestrictDelete = "ReferentialRestrictDelete";

    /// <summary>Parental — cascades Assign/Share/Unshare/Reparent/Delete/Merge from parent to child. An entity can only be the child in one Parental relationship at a time; Dataverse rejects a second.</summary>
    public const string Parental = "Parental";

    /// <summary>Every recognized behavior name — used for the generated JSON Schema's own <c>enum</c> on <see cref="Conversion.Models.AttributeDefinition.RelationshipBehavior"/> (see <see cref="Schema.SchemaEnumAttribute"/>) and by <see cref="AttributeChangeValidator"/> to reject a typo before this tool ever builds a request.</summary>
    public static readonly IReadOnlyList<string> Names = [Referential, ReferentialRestrictDelete, Parental];

    /// <returns>The <c>CascadeConfiguration</c> body for <paramref name="behavior"/> (case-insensitive), or null when it isn't one of <see cref="Names"/>.</returns>
    public static JsonObject? CascadeConfigurationOrNull(string behavior)
    {
        // Every field here follows Microsoft's own documented
        // "Parental"/"Not Parental" table (see this class's own Referential
        // doc comment for the citation): Referential/ReferentialRestrictDelete
        // keep Assign/Share/Unshare/Reparent at NoCascade and Merge at
        // NoCascade too (confirmed against a real Referential relationship's
        // own live CascadeConfiguration — Merge isn't a "Not Parental"
        // requirement, but NoCascade is genuinely what a plain lookup gets,
        // not Cascade), differing only in Delete: RemoveLink (unlink
        // children) vs Restrict (block the delete outright). Parental
        // cascades everything, including Merge.
        return behavior.ToUpperInvariant() switch
        {
            "REFERENTIAL" => Build(assign: "NoCascade", delete: "RemoveLink", merge: "NoCascade", reparent: "NoCascade", share: "NoCascade", unshare: "NoCascade"),
            "REFERENTIALRESTRICTDELETE" => Build(assign: "NoCascade", delete: "Restrict", merge: "NoCascade", reparent: "NoCascade", share: "NoCascade", unshare: "NoCascade"),
            "PARENTAL" => Build(assign: "Cascade", delete: "Cascade", merge: "Cascade", reparent: "Cascade", share: "Cascade", unshare: "Cascade"),
            _ => null,
        };
    }

    private static JsonObject Build(string assign, string delete, string merge, string reparent, string share, string unshare) => new()
    {
        ["Assign"] = assign,
        ["Delete"] = delete,
        ["Merge"] = merge,
        ["Reparent"] = reparent,
        ["Share"] = share,
        ["Unshare"] = unshare,
    };
}

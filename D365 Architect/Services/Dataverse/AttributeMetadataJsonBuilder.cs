using System.Text.Json.Nodes;
using D365Architect.Services.Conversion.Models;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// Builds/mutates the JSON bodies Dataverse's Web API needs to create or
/// update a column — confirmed against Microsoft's own documented
/// create/update examples for each type covered, not guessed. See
/// <see cref="SupportedTypes"/> for exactly which types that is and
/// `docs/yaml-conventions.md`'s "Importing tables" section for why the rest
/// are deliberately excluded rather than attempted anyway.
///
/// Update is a full-object replace, not a partial patch: Dataverse's own
/// docs are explicit that <c>PUT</c> on a column "can't update individual
/// properties" — you must send back the entire current definition with
/// only the fields you actually want changed edited. <see cref="ApplyUpdateFields"/>
/// takes that full live representation (fetched immediately beforehand)
/// and mutates only the fields this tool tracks, in place — the same
/// retrieve-and-patch principle <see cref="Conversion.FormXmlWriter"/>
/// already applies to FormXML, applied here to a JSON object instead of an
/// XML document.
/// </summary>
public static class AttributeMetadataJsonBuilder
{
    /// <summary>
    /// Every attribute type this tool can safely create or update.
    /// Deliberately excludes: <c>Picklist</c>/<c>Boolean</c> (need an
    /// <c>OptionSet</c> definition this tool doesn't capture on export at
    /// all yet — see <see cref="Conversion.EntityJsonDefinitionReader"/>'s
    /// own doc comment); <c>Lookup</c>/<c>Customer</c>/<c>Owner</c> (not
    /// creatable via this endpoint at all — Microsoft's own docs confirm a
    /// Lookup attribute only comes into existence as part of creating a
    /// whole relationship, a materially different and much larger
    /// operation this tool doesn't attempt); <c>Double</c> (no official
    /// Microsoft example of its create shape could be found — every other
    /// type here is confirmed against one, and guessing at API shapes that
    /// could corrupt a live table's schema isn't a risk worth taking); and
    /// anything else not investigated at all
    /// (<c>MultiSelectPicklist</c>/<c>State</c>/<c>Status</c>/
    /// <c>Uniqueidentifier</c>/<c>PartyList</c>/<c>File</c>/<c>Image</c>/
    /// <c>Virtual</c>/<c>EntityName</c>/<c>ManagedProperty</c>).
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedTypes = new HashSet<string>
    {
        "String", "Memo", "Integer", "BigInt", "Decimal", "Money", "DateTime",
    };

    /// <summary>
    /// Builds a brand-new attribute's create body from this tool's own
    /// curated <see cref="AttributeDefinition"/> — only ever called for an
    /// attribute that doesn't exist live yet, and only for a type in
    /// <see cref="SupportedTypes"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="attribute"/> has no <see cref="AttributeDefinition.SchemaName"/> — required to create a column, and never inferred.</exception>
    /// <exception cref="NotSupportedException"><paramref name="attribute"/>'s own <see cref="AttributeDefinition.Type"/> isn't in <see cref="SupportedTypes"/>.</exception>
    public static JsonObject BuildCreateBody(AttributeDefinition attribute)
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

            default:
                throw new NotSupportedException($"'{attribute.Type}' isn't one of the attribute types this tool can create yet — see {nameof(AttributeMetadataJsonBuilder)}.{nameof(SupportedTypes)}.");
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
    /// </summary>
    /// <exception cref="NotSupportedException"><paramref name="attribute"/>'s own <see cref="AttributeDefinition.Type"/> isn't in <see cref="SupportedTypes"/>.</exception>
    public static void ApplyUpdateFields(JsonObject existing, AttributeDefinition attribute)
    {
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

            default:
                throw new NotSupportedException($"'{attribute.Type}' isn't one of the attribute types this tool can update yet — see {nameof(AttributeMetadataJsonBuilder)}.{nameof(SupportedTypes)}.");
        }
    }
}

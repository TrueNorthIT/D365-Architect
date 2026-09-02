using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace D365Architect.Services.Schema;

/// <summary>
/// Builds a JSON Schema describing one of this tool's curated YAML shapes
/// (e.g. <see cref="EntityDefinition"/>/<see cref="AttributeDefinition"/> for
/// tables, <see cref="ViewDefinition"/> for views, <see cref="FormDefinition"/>
/// for forms) by reflecting over
/// the model classes directly, rather than hand-maintaining a second copy of
/// each shape that could drift out of sync:
///
/// - Property names come from the same <see cref="YamlMemberAttribute"/>
///   alias/casing the matching <c>*YamlSerializer</c> uses to write the YAML
///   in the first place.
/// - "required" comes from the C# <c>required</c> modifier.
/// - Descriptions come from this assembly's own XML doc comments (see
///   <c>GenerateDocumentationFile</c> in the project file) — the same text
///   a developer reading the model source already sees.
/// </summary>
public static class YamlSchemaGenerator
{
    /// <param name="rootType">The curated model type to generate a schema for, e.g. <see cref="EntityDefinition"/>.</param>
    /// <param name="title">The schema's "title".</param>
    /// <param name="description">The schema's "description".</param>
    public static JsonObject Generate(Type rootType, string title, string description)
    {
        var xmlDocs = TryLoadXmlDocs();
        var schema = BuildObjectSchema(rootType, xmlDocs, []);

        schema.Insert(0, "$schema", "https://json-schema.org/draft/2020-12/schema");
        schema.Insert(1, "title", title);
        schema.Insert(2, "description", description);

        return schema;
    }

    private static JsonObject BuildObjectSchema(Type type, XDocument? xmlDocs, HashSet<Type> ancestors)
    {
        if (!ancestors.Add(type))
        {
            throw new NotSupportedException($"Circular reference through '{type.Name}' — this generator doesn't support recursive shapes.");
        }

        var properties = new JsonObject();
        var required = new JsonArray();

        var ordered = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Member: property.GetCustomAttribute<YamlMemberAttribute>()))
            .OrderBy(p => p.Member?.Order ?? int.MaxValue);

        foreach (var (property, member) in ordered)
        {
            var name = member?.Alias ?? CamelCaseNamingConvention.Instance.Apply(property.Name);
            var description = GetDescription(type, property, xmlDocs);

            properties[name] = BuildPropertySchema(property, description, xmlDocs, ancestors);

            if (property.GetCustomAttribute<RequiredMemberAttribute>() is not null)
            {
                required.Add(name);
            }
        }

        ancestors.Remove(type);

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static JsonObject BuildPropertySchema(PropertyInfo property, string? description, XDocument? xmlDocs, HashSet<Type> ancestors)
    {
        var propertyType = property.PropertyType;
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // Checked before the ordinary type-driven switch below: a property
        // marked SchemaEnum still gets its base "type": "string" from that
        // switch, just with an "enum" added on top constraining it to a
        // known set of values — see SchemaEnumAttribute's own doc comment
        // for why this reads the values by reflection rather than they
        // being duplicated here by hand.
        if (property.GetCustomAttribute<SchemaEnumAttribute>() is { } schemaEnum)
        {
            var values = (IEnumerable<string>)(schemaEnum.ProviderType.GetMember(schemaEnum.ProviderMemberName, BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault()
                switch
                {
                    FieldInfo field => field.GetValue(null),
                    PropertyInfo prop => prop.GetValue(null),
                    _ => throw new InvalidOperationException($"'{schemaEnum.ProviderType.Name}.{schemaEnum.ProviderMemberName}' (named by a {nameof(SchemaEnumAttribute)} on '{property.DeclaringType?.Name}.{property.Name}') isn't a public static field or property."),
                })!;

            var enumSchema = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => JsonValue.Create(v)).ToArray()) };
            if (!string.IsNullOrWhiteSpace(description))
            {
                enumSchema.Insert(0, "description", description);
            }

            return enumSchema;
        }

        var schema = BuildValueSchema(type, xmlDocs, ancestors);

        if (!string.IsNullOrWhiteSpace(description))
        {
            schema.Insert(0, "description", description);
        }

        return schema;
    }

    /// <summary>
    /// The type-driven half of <see cref="BuildPropertySchema"/>, split out
    /// so a dictionary's own value type (see below) can recurse into it
    /// without re-deriving a property's <see cref="SchemaEnumAttribute"/>/
    /// description, neither of which a dictionary value has of its own.
    /// </summary>
    private static JsonObject BuildValueSchema(Type type, XDocument? xmlDocs, HashSet<Type> ancestors) => type switch
    {
        _ when type == typeof(string) => new JsonObject { ["type"] = "string" },
        _ when type == typeof(int) => new JsonObject { ["type"] = "integer" },
        _ when type == typeof(double) => new JsonObject { ["type"] = "number" },
        _ when type == typeof(bool) => new JsonObject { ["type"] = "boolean" },
        // YamlDotNet (and this tool's own YAML) renders a Guid as a plain
        // hyphenated string, same as everywhere else this tool writes
        // one out — "format": "uuid" is JSON Schema's own annotation for
        // that shape, not a stricter type of its own.
        _ when type == typeof(Guid) => new JsonObject { ["type"] = "string", ["format"] = "uuid" },
        // A genuinely dynamic shape (e.g. FormControl.Parameters, converted
        // structurally from arbitrary XML rather than a fixed model) has no
        // fixed schema of its own to describe — an empty schema is JSON
        // Schema's own way of saying "any value is valid here", which is
        // honest about that rather than asserting a shape that isn't real.
        _ when type == typeof(object) => new JsonObject(),
        // A dictionary (e.g. FormControl.Translations, keyed by languagecode)
        // is a YAML/JSON mapping with arbitrary keys, not a fixed set of
        // named properties — checked before the generic IEnumerable case
        // below, which a dictionary also matches: that path assumes exactly
        // one generic argument (an element type) and silently produced an
        // empty object schema for a dictionary's two (key and value) until
        // this was added. JSON Schema has no notion of a non-string key, so
        // the key type itself isn't represented — only the value type is.
        _ when GetDictionaryValueType(type) is { } dictionaryValueType => new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = BuildValueSchema(dictionaryValueType, xmlDocs, ancestors),
        },
        _ when type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) => BuildArraySchema(type, xmlDocs, ancestors),
        // A curated model type (e.g. FormDisplayCondition) used as a
        // single property rather than always inside a list — recurse
        // the same way BuildArraySchema does for its element type.
        _ when type.IsClass => BuildObjectSchema(type, xmlDocs, ancestors),
        _ => throw new NotSupportedException($"No JSON Schema mapping for type '{type}'. Extend {nameof(YamlSchemaGenerator)} to handle it."),
    };

    private static Type? GetDictionaryValueType(Type type) =>
        type.GetInterfaces().Prepend(type)
            .FirstOrDefault(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
            is { } dictionaryInterface && dictionaryInterface.GetGenericArguments() is [_, var valueType]
            ? valueType
            : null;

    private static JsonObject BuildArraySchema(Type enumerableType, XDocument? xmlDocs, HashSet<Type> ancestors)
    {
        var elementType = enumerableType.GetGenericArguments() is [var element] ? element : typeof(object);

        return new JsonObject
        {
            ["type"] = "array",
            ["items"] = elementType == typeof(string)
                ? new JsonObject { ["type"] = "string" }
                : BuildObjectSchema(elementType, xmlDocs, ancestors),
        };
    }

    private static XDocument? TryLoadXmlDocs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{typeof(YamlSchemaGenerator).Assembly.GetName().Name}.xml");
        return File.Exists(path) ? XDocument.Load(path) : null;
    }

    private static string? GetDescription(Type type, PropertyInfo property, XDocument? xmlDocs)
    {
        var summary = xmlDocs?.Descendants("member")
            .FirstOrDefault(m => (string?)m.Attribute("name") == $"P:{type.FullName}.{property.Name}")
            ?.Element("summary");

        return summary is null ? null : CleanSummary(summary);
    }

    /// <summary>
    /// Flattens an XML doc &lt;summary&gt; into plain text: collapses the
    /// comment's own indentation/line breaks, and replaces `&lt;see cref="X"/&gt;`
    /// references with just the referenced member's name (its raw cref, e.g.
    /// "P:Namespace.Type.Member", has no text content of its own).
    /// </summary>
    private static string CleanSummary(XElement summary)
    {
        var text = new StringBuilder();

        foreach (var node in summary.Nodes())
        {
            text.Append(node switch
            {
                XElement { Name.LocalName: "see" } see => (string?)see.Attribute("cref") is { } cref ? CrefToMemberName(cref) : see.Value,
                XText t => t.Value,
                XElement e => e.Value,
                _ => "",
            });
        }

        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Reduces a cref like "P:Namespace.Type.Member" or (for a method,
    /// which embeds its full parameter list, dots and all — e.g.
    /// "M:Namespace.Type.Method(System.Nullable{System.Boolean})") down to
    /// just the member's own name. The parameter list has to be stripped
    /// before splitting on '.', or a parameter type's own namespace can
    /// masquerade as "the last segment" and end up in the generated schema
    /// instead of the actual member name.
    /// </summary>
    private static string CrefToMemberName(string cref) => cref.Split('(')[0].Split('.')[^1];
}

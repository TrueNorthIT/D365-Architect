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

            properties[name] = BuildPropertySchema(property.PropertyType, description, xmlDocs, ancestors);

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

    private static JsonObject BuildPropertySchema(Type propertyType, string? description, XDocument? xmlDocs, HashSet<Type> ancestors)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        var schema = type switch
        {
            _ when type == typeof(string) => new JsonObject { ["type"] = "string" },
            _ when type == typeof(int) => new JsonObject { ["type"] = "integer" },
            _ when type == typeof(double) => new JsonObject { ["type"] = "number" },
            _ when type == typeof(bool) => new JsonObject { ["type"] = "boolean" },
            _ when type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) => BuildArraySchema(type, xmlDocs, ancestors),
            _ => throw new NotSupportedException($"No JSON Schema mapping for type '{type}'. Extend {nameof(YamlSchemaGenerator)} to handle it."),
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            schema.Insert(0, "description", description);
        }

        return schema;
    }

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
                XElement { Name.LocalName: "see" } see => (string?)see.Attribute("cref") is { } cref ? cref.Split('.')[^1] : see.Value,
                XText t => t.Value,
                XElement e => e.Value,
                _ => "",
            });
        }

        return string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

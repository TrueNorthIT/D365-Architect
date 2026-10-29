namespace D365Architect.Services.Schema;

/// <summary>
/// Marks a curated model class that its YAML may also write as a plain
/// string (its own custom type converter reads both shapes — e.g.
/// <see cref="Conversion.Models.ViewColumn"/>, where <c>- createdon</c> is
/// shorthand for <c>- name: createdon</c>). <see cref="YamlSchemaGenerator"/>
/// then emits <c>anyOf: [string, the object]</c> instead of the object alone,
/// so an editor doesn't flag the shorthand as invalid.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SchemaPlainStringShorthandAttribute : Attribute;

namespace D365Architect.Services.Schema;

/// <summary>
/// Marks a curated model's string property as constrained to a fixed set
/// of values — <see cref="YamlSchemaGenerator"/> emits them as the
/// generated JSON Schema's own <c>enum</c> instead of an open string, so
/// an editor (e.g. VS Code's YAML extension, already wired to these
/// generated schemas — see `README.md`) can offer autocomplete and flag a
/// typo before this tool ever gets a chance to. The values themselves
/// come from evaluating <paramref name="providerMemberName"/> on
/// <paramref name="providerType"/> via reflection at schema-generation
/// time, rather than being duplicated by hand here — so the schema can
/// never drift out of sync with whatever this tool's own validation
/// actually accepts.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SchemaEnumAttribute(Type providerType, string providerMemberName) : Attribute
{
    public Type ProviderType { get; } = providerType;
    public string ProviderMemberName { get; } = providerMemberName;
}

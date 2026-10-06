using D365Architect.Services.Conversion.Models;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// A small mutable builder for <see cref="AttributeDefinition"/> test
/// fixtures, so a test can set only the fields it actually cares about
/// rather than repeating every property name of the (required Name/Type,
/// otherwise all-optional) init-only model every time.
/// </summary>
public sealed class AttributeDefinitionBuilder(string type, string? schemaName = "tn_Test")
{
    public string Name { get; set; } = "tn_test";
    public string Type { get; set; } = type;
    public string? SchemaName { get; set; } = schemaName;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? RequiredLevel { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? PrecisionSource { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public string? Format { get; set; }
    public IReadOnlyList<string>? Targets { get; set; }
    public IReadOnlyList<AttributeOptionDefinition>? Options { get; set; }
    public string? GlobalOptionSetName { get; set; }
    public bool? DefaultValue { get; set; }
    public string? TrueOptionLabel { get; set; }
    public string? FalseOptionLabel { get; set; }
    public string? RelationshipSchemaName { get; set; }
    public string? RelationshipBehavior { get; set; }

    public AttributeDefinition Build() => new()
    {
        Name = Name,
        Type = Type,
        SchemaName = SchemaName,
        DisplayName = DisplayName,
        Description = Description,
        RequiredLevel = RequiredLevel,
        MaxLength = MaxLength,
        Precision = Precision,
        PrecisionSource = PrecisionSource,
        MinValue = MinValue,
        MaxValue = MaxValue,
        Format = Format,
        Targets = Targets,
        Options = Options,
        GlobalOptionSetName = GlobalOptionSetName,
        DefaultValue = DefaultValue,
        TrueOptionLabel = TrueOptionLabel,
        FalseOptionLabel = FalseOptionLabel,
        RelationshipSchemaName = RelationshipSchemaName,
        RelationshipBehavior = RelationshipBehavior,
    };

    public static AttributeDefinition Create(string type, string? schemaName = "tn_Test", Action<AttributeDefinitionBuilder>? configure = null)
    {
        var builder = new AttributeDefinitionBuilder(type, schemaName);
        configure?.Invoke(builder);
        return builder.Build();
    }
}

using System.Text.Json.Nodes;

namespace D365Architect.Services.Dataverse;

/// <summary>
/// One pending Dataverse metadata write, described declaratively rather than
/// as an already-issued call — what <see cref="IDataverseClient.ExecuteTransactionAsync"/>
/// takes so a caller (e.g. <c>TableImportService</c>) can hand over a whole
/// column plan's worth of writes to run as one atomic changeset, instead of
/// awaiting each individual <c>IDataverseClient</c> write method in turn.
/// Closed hierarchy (private constructor) — every case below is one of the
/// writes <see cref="DataverseClient"/> already exposes as its own
/// individual method (<see cref="IDataverseClient.CreateAttributeAsync"/>
/// and friends); this is deliberately not a general-purpose "any Dataverse
/// write" type.
/// </summary>
public abstract record DataverseWrite
{
    private DataverseWrite()
    {
    }

    /// <summary>See <see cref="IDataverseClient.UpdateEntityAsync"/>.</summary>
    public sealed record UpdateEntity(string EntityLogicalName, JsonObject Metadata) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.CreateAttributeAsync"/>.</summary>
    public sealed record CreateAttribute(string EntityLogicalName, JsonObject Metadata) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.UpdateAttributeAsync"/>.</summary>
    public sealed record UpdateAttribute(string EntityLogicalName, string AttributeLogicalName, JsonObject Metadata) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.CreateOneToManyRelationshipAsync"/>.</summary>
    public sealed record CreateOneToManyRelationship(JsonObject Metadata) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.CreateCustomerRelationshipsAsync"/>.</summary>
    public sealed record CreateCustomerRelationships(JsonObject Body) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.InsertOptionValueAsync"/>.</summary>
    public sealed record InsertOptionValue(JsonObject Body) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.UpdateOptionValueAsync"/>.</summary>
    public sealed record UpdateOptionValue(JsonObject Body) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.OrderOptionsAsync"/>.</summary>
    public sealed record OrderOptions(JsonObject Body) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.InsertStatusValueAsync"/>.</summary>
    public sealed record InsertStatusValue(JsonObject Body) : DataverseWrite;

    /// <summary>See <see cref="IDataverseClient.UpdateStateValueAsync"/>.</summary>
    public sealed record UpdateStateValue(JsonObject Body) : DataverseWrite;

    /// <summary>A short, human-readable label for error messages when a changeset is rolled back — e.g. "create column 'tn_foo'".</summary>
    public string Describe() => this switch
    {
        UpdateEntity w => $"update table '{w.EntityLogicalName}'",
        CreateAttribute w => $"create column on '{w.EntityLogicalName}'",
        UpdateAttribute w => $"update column '{w.AttributeLogicalName}' on '{w.EntityLogicalName}'",
        CreateOneToManyRelationship => "create lookup relationship",
        CreateCustomerRelationships => "create customer relationship",
        InsertOptionValue => "insert option",
        UpdateOptionValue => "update option",
        OrderOptions => "reorder options",
        InsertStatusValue => "insert status value",
        UpdateStateValue => "update state value",
        _ => GetType().Name,
    };
}

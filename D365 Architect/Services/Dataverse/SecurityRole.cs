namespace D365Architect.Services.Dataverse;

/// <summary>A security role of the root business unit, identified across environments by <paramref name="Name"/> (the id differs per environment).</summary>
public sealed record SecurityRole(Guid RoleId, string Name);

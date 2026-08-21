namespace D365Architect.Services.Dataverse;

public sealed record WhoAmIResult(Guid UserId, Guid BusinessUnitId, Guid OrganizationId);

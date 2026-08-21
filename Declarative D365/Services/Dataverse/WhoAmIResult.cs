namespace Declarative_D365.Services.Dataverse;

public sealed record WhoAmIResult(Guid UserId, Guid BusinessUnitId, Guid OrganizationId);

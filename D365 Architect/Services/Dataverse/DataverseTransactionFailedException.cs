namespace D365Architect.Services.Dataverse;

/// <summary>
/// Thrown by <see cref="IDataverseClient.ExecuteTransactionAsync"/> when a changeset is rolled back.
/// <see cref="FailedContentId"/> is the <c>Content-ID</c> of the request Dataverse reported as the failing one
/// (requests are numbered from 1, in the order of the writes passed in), so a caller can map the failure back
/// to the write - e.g. <c>TableCreateService.ApplyAllAsync</c> maps it back to a table. Null when the response
/// didn't say which request failed.
/// </summary>
public sealed class DataverseTransactionFailedException(string message, int? failedContentId, Exception? inner = null)
    : HttpRequestException(message, inner)
{
    public int? FailedContentId { get; } = failedContentId;
}

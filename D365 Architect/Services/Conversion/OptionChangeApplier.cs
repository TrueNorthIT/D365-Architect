using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

/// <summary>
/// Dispatches one <see cref="OptionChangePlan"/> to the matching
/// <see cref="IDataverseClient"/> action — shared by
/// <see cref="TableImportService"/> (a column's options) and
/// <c>GlobalChoiceImportService</c> (a global choice's own options), since
/// both only ever differ in how the plan's own <see cref="OptionChangePlan.RequestBody"/>
/// was built (see <see cref="OptionSetDiffer"/>), never in which action a
/// given <see cref="OptionChangeAction"/> maps to.
/// </summary>
internal static class OptionChangeApplier
{
    public static async Task ApplyAsync(IDataverseClient dataverseClient, Uri environmentUrl, string accessToken, OptionChangePlan change, CancellationToken cancellationToken)
    {
        switch (change.Action)
        {
            case OptionChangeAction.InsertOption:
                await dataverseClient.InsertOptionValueAsync(environmentUrl, accessToken, change.RequestBody, cancellationToken);
                break;

            case OptionChangeAction.UpdateOption:
                await dataverseClient.UpdateOptionValueAsync(environmentUrl, accessToken, change.RequestBody, cancellationToken);
                break;

            case OptionChangeAction.OrderOptions:
                await dataverseClient.OrderOptionsAsync(environmentUrl, accessToken, change.RequestBody, cancellationToken);
                break;

            case OptionChangeAction.UpdateStateValue:
                await dataverseClient.UpdateStateValueAsync(environmentUrl, accessToken, change.RequestBody, cancellationToken);
                break;

            case OptionChangeAction.InsertStatusValue:
                await dataverseClient.InsertStatusValueAsync(environmentUrl, accessToken, change.RequestBody, cancellationToken);
                break;
        }
    }
}

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

    /// <summary>The <see cref="DataverseWrite"/> counterpart to <see cref="ApplyAsync"/>'s own switch — same action-to-call mapping, for a caller (<see cref="TableImportService"/>) batching this into a transaction instead of calling it directly.</summary>
    public static DataverseWrite ToWrite(OptionChangePlan change) => change.Action switch
    {
        OptionChangeAction.InsertOption => new DataverseWrite.InsertOptionValue(change.RequestBody),
        OptionChangeAction.UpdateOption => new DataverseWrite.UpdateOptionValue(change.RequestBody),
        OptionChangeAction.OrderOptions => new DataverseWrite.OrderOptions(change.RequestBody),
        OptionChangeAction.UpdateStateValue => new DataverseWrite.UpdateStateValue(change.RequestBody),
        OptionChangeAction.InsertStatusValue => new DataverseWrite.InsertStatusValue(change.RequestBody),
        _ => throw new NotSupportedException($"Unhandled {nameof(OptionChangeAction)}: {change.Action}"),
    };
}

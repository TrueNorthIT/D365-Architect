using Xunit;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Every live test class joins this collection (<c>[Collection(LiveCollection.Name)]</c>) so they run one at a time:
/// concurrent metadata writes to one environment fail with Dataverse's <c>CustomizationLockException</c> (429).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveCollection
{
    public const string Name = "Live";
}

using Xunit;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Shared checks for the publish tests ("publish by default"): what was
/// or wasn't published, stated in the requirements' own terms.
/// </summary>
public static class PublishAssertions
{
    /// <summary>Nothing at all went out to PublishXml, whether for tables or for global choices.</summary>
    public static void NothingPublished(FakeDataverseClient client, string because)
    {
        Expect.Empty(client.PublishEntityCalls, $"{because} (tables published)");
        Assert.True(client.PublishGlobalOptionSetsCalls.Count == 0, $"{because} (global choices published: <{string.Join(" | ", client.PublishGlobalOptionSetsCalls.Select(c => string.Join(", ", c)))}>).");
    }
}

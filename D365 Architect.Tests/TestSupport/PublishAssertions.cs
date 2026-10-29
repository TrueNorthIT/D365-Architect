using Xunit;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Shared checks for the <c>--publish</c> tests (REQ-34 to REQ-39): what a
/// publish-only run must not have written, and what it must or must not have
/// published, stated in the requirements' own terms.
/// </summary>
public static class PublishAssertions
{
    /// <summary>REQ-34: "With it the command only publishes; it writes no other changes" - no create, update, seed, option or relationship write of any kind reached Dataverse.</summary>
    public static void NothingWritten(FakeDataverseClient client, string requirement)
    {
        var because = $"{requirement}: '--publish' only publishes, so no create or update should reach Dataverse.";
        Expect.Empty(client.CreateEntityCalls, $"{because} (table creates)");
        Expect.Empty(client.UpdateEntityCalls, $"{because} (table updates)");
        Expect.Empty(client.CreateAttributeCalls, $"{because} (column creates)");
        Expect.Empty(client.UpdateAttributeCalls, $"{because} (column updates)");
        Expect.Empty(client.CreateOneToManyRelationshipCalls, $"{because} (lookup relationships)");
        Expect.Empty(client.CreateCustomerRelationshipsCalls, $"{because} (customer relationships)");
        Expect.Empty(client.InsertOptionValueCalls, $"{because} (option inserts)");
        Expect.Empty(client.UpdateOptionValueCalls, $"{because} (option updates)");
        Expect.Empty(client.OrderOptionsCalls, $"{because} (option ordering)");
        Expect.Empty(client.SetAutoNumberSeedCalls, $"{because} (auto-number seeds)");
        Expect.Empty(client.CreateGlobalOptionSetCalls, $"{because} (choice creates)");
        Expect.Empty(client.UpdateGlobalOptionSetCalls, $"{because} (choice updates)");
        Expect.Empty(client.CreateSavedQueryCalls, $"{because} (view creates)");
        Expect.Empty(client.UpdateSavedQueryCalls, $"{because} (view updates)");
        Expect.Empty(client.UpdateSystemFormXmlCalls, $"{because} (form updates)");
        Expect.Empty(client.WriteOrder, $"{because} (any table, view or form write)");
        Expect.Empty(client.ColumnWriteOrder, $"{because} (any column write)");
    }

    /// <summary>Nothing at all went out to PublishXml, whether for tables or for global choices.</summary>
    public static void NothingPublished(FakeDataverseClient client, string requirement, string because)
    {
        Expect.Empty(client.PublishEntityCalls, $"{requirement}: {because} (tables published)");
        Assert.True(client.PublishGlobalOptionSetsCalls.Count == 0, $"{requirement}: {because} (global choices published: <{string.Join(" | ", client.PublishGlobalOptionSetsCalls.Select(c => string.Join(", ", c)))}>).");
    }
}

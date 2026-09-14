using D365Architect.Services.Dataverse;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

public sealed class DataverseLabelJsonTests
{
    [Fact]
    public void Build_ProducesEnglishLocalizedLabel()
    {
        var label = DataverseLabelJson.Build("Hello");

        Assert.Equal("Microsoft.Dynamics.CRM.Label", (string)label["@odata.type"]!);
        var localized = label["LocalizedLabels"]!.AsArray();
        var entry = Assert.Single(localized);
        Assert.Equal("Hello", (string)entry!["Label"]!);
        Assert.Equal(1033, (int)entry["LanguageCode"]!);
        Assert.Equal("Microsoft.Dynamics.CRM.LocalizedLabel", (string)entry["@odata.type"]!);
    }
}

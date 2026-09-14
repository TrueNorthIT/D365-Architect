using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

public sealed class StandardFormControlsTests
{
    private const string SingleLineTextClassId = "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}";

    [Fact]
    public void TryGetClassId_KnownName_ReturnsBracedUppercaseGuid()
    {
        Assert.Equal(SingleLineTextClassId, StandardFormControls.TryGetClassId("SingleLineText"));
    }

    [Fact]
    public void TryGetClassId_UnknownName_ReturnsNull()
    {
        Assert.Null(StandardFormControls.TryGetClassId("NotARealControl"));
    }

    [Fact]
    public void TryGetFriendlyName_KnownClassId_ReturnsFriendlyName()
    {
        Assert.Equal("SingleLineText", StandardFormControls.TryGetFriendlyName(SingleLineTextClassId));
    }

    [Fact]
    public void TryGetFriendlyName_KnownClassId_IsCaseInsensitiveAndBraceInsensitive()
    {
        Assert.Equal("SingleLineText", StandardFormControls.TryGetFriendlyName("4273edbd-ac1d-40d3-9fb2-095c621b552d"));
    }

    [Fact]
    public void TryGetFriendlyName_UnknownClassId_ReturnsNull()
    {
        Assert.Null(StandardFormControls.TryGetFriendlyName("{00000000-0000-0000-0000-000000000000}"));
    }

    [Fact]
    public void TryGetFriendlyName_NotEvenAGuid_ReturnsNull()
    {
        Assert.Null(StandardFormControls.TryGetFriendlyName("not-a-guid"));
    }

    [Fact]
    public void IsKnownFriendlyName_KnownAndUnknown()
    {
        Assert.True(StandardFormControls.IsKnownFriendlyName("SingleLineText"));
        Assert.False(StandardFormControls.IsKnownFriendlyName("NotARealControl"));
    }

    [Fact]
    public void FriendlyNames_IsSortedAndContainsKnownEntries()
    {
        Assert.Contains("SingleLineText", StandardFormControls.FriendlyNames);
        Assert.Equal(StandardFormControls.FriendlyNames.OrderBy(n => n, StringComparer.Ordinal), StandardFormControls.FriendlyNames);
    }

    // ---- Resolve priority: Control > CustomControlId > ClassId ----

    [Fact]
    public void Resolve_ControlSet_ReturnsItsClassId()
    {
        var control = new FormControl { Id = "x", Control = "SingleLineText" };
        Assert.Equal(SingleLineTextClassId, StandardFormControls.Resolve(control));
    }

    [Fact]
    public void Resolve_ControlSetToUnrecognizedName_ReturnsNull()
    {
        // Not this method's job to flag it - see FormControlValidator instead.
        var control = new FormControl { Id = "x", Control = "NotARealControl" };
        Assert.Null(StandardFormControls.Resolve(control));
    }

    [Fact]
    public void Resolve_CustomControlIdOnly_ReturnsItVerbatim()
    {
        var control = new FormControl { Id = "x", CustomControlId = "{some-pcf-guid}" };
        Assert.Equal("{some-pcf-guid}", StandardFormControls.Resolve(control));
    }

    [Fact]
    public void Resolve_LegacyClassIdOnly_ReturnsItVerbatim()
    {
        var control = new FormControl { Id = "x", ClassId = "{legacy-guid}" };
        Assert.Equal("{legacy-guid}", StandardFormControls.Resolve(control));
    }

    [Fact]
    public void Resolve_ControlTakesPriorityOverCustomControlId()
    {
        var control = new FormControl { Id = "x", Control = "SingleLineText", CustomControlId = "{some-pcf-guid}" };
        Assert.Equal(SingleLineTextClassId, StandardFormControls.Resolve(control));
    }

    [Fact]
    public void Resolve_CustomControlIdTakesPriorityOverLegacyClassId()
    {
        var control = new FormControl { Id = "x", CustomControlId = "{custom-guid}", ClassId = "{legacy-guid}" };
        Assert.Equal("{custom-guid}", StandardFormControls.Resolve(control));
    }

    [Fact]
    public void Resolve_NothingSet_ReturnsNull()
    {
        var control = new FormControl { Id = "x" };
        Assert.Null(StandardFormControls.Resolve(control));
    }
}

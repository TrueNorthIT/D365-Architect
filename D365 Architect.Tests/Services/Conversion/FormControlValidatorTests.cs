using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="FormControlValidator"/>, including the round-10
/// regression: a genuinely invalid control (<c>Control</c>/<c>CustomControlId</c>
/// both set) must always be reported, which only matters once the caller
/// (<see cref="Commands.Form.ImportFormCommand"/>) stops gating that report
/// on whether the rendered FormXML also happens to differ — see
/// <c>ImportFormCommand</c>'s own <c>SkipBeforePrinting</c> for where that's
/// actually wired up; this class's own job is just to keep reporting the
/// violation regardless.
/// </summary>
public sealed class FormControlValidatorTests
{
    private static FormDefinition FormWith(FormControl control) => new()
    {
        Name = "Test Form",
        Entity = "tn_test",
        Tabs =
        [
            new FormTab
            {
                Columns =
                [
                    new FormColumn
                    {
                        Sections = [new FormSection { Controls = [control] }],
                    },
                ],
            },
        ],
    };

    private static readonly XElement EmptyExistingRoot = new("form", new XElement("tabs"));

    [Fact]
    public void Validate_BothControlAndCustomControlIdSet_ReportsMutuallyExclusiveError()
    {
        var control = new FormControl { Id = "tn_field", Control = "SingleLineText", CustomControlId = "{00000000-0000-0000-0000-000000000001}" };

        var messages = FormControlValidator.Validate(FormWith(control), EmptyExistingRoot);

        var message = Assert.Single(messages);
        Assert.Contains("mutually exclusive", message.Message);
    }

    [Fact]
    public void Validate_UnrecognizedControlName_ReportsTypoError()
    {
        var control = new FormControl { Id = "tn_field", Control = "ThisIsNotARealControlName" };

        var messages = FormControlValidator.Validate(FormWith(control), EmptyExistingRoot);

        var message = Assert.Single(messages);
        Assert.Contains("recognized standard control", message.Message);
    }

    [Fact]
    public void Validate_RecognizedControlName_ProducesNoViolation()
    {
        var control = new FormControl { Id = "tn_field", Control = "SingleLineText" };
        Assert.Empty(FormControlValidator.Validate(FormWith(control), EmptyExistingRoot));
    }

    [Fact]
    public void Validate_CustomControlIdOnly_ProducesNoViolation()
    {
        var control = new FormControl { Id = "tn_field", CustomControlId = "{00000000-0000-0000-0000-000000000001}" };
        Assert.Empty(FormControlValidator.Validate(FormWith(control), EmptyExistingRoot));
    }

    [Fact]
    public void Validate_NoResolvableClassId_AndExistingControlAlsoLacksOne_IsExempted()
    {
        var control = new FormControl { Id = "tn_field" }; // no Control/CustomControlId/ClassId at all
        var existingRoot = new XElement("form",
            new XElement("tabs", new XElement("tab", new XElement("control", new XAttribute("id", "tn_field")))));

        Assert.Empty(FormControlValidator.Validate(FormWith(control), existingRoot));
    }

    [Fact]
    public void Validate_NoResolvableClassId_AndExistingControlHasOne_ReportsError()
    {
        var control = new FormControl { Id = "tn_field" };
        var existingRoot = new XElement("form",
            new XElement("tabs", new XElement("tab",
                new XElement("control", new XAttribute("id", "tn_field"), new XAttribute("classid", "{11111111-1111-1111-1111-111111111111}")))));

        var messages = FormControlValidator.Validate(FormWith(control), existingRoot);

        var message = Assert.Single(messages);
        Assert.Contains("class id cannot be null", message.Message);
    }

    [Fact]
    public void Validate_NoResolvableClassId_AndControlIsBrandNew_ReportsError()
    {
        // Not present in the existing document at all - never exempted.
        var control = new FormControl { Id = "tn_brand_new_field" };
        Assert.Single(FormControlValidator.Validate(FormWith(control), EmptyExistingRoot));
    }

    [Fact]
    public void Validate_HeaderAndFooterControls_AreAlsoChecked()
    {
        var form = new FormDefinition
        {
            Name = "Test Form",
            Entity = "tn_test",
            HeaderControls = [new FormControl { Id = "header_field", Control = "SingleLineText", CustomControlId = "{00000000-0000-0000-0000-000000000001}" }],
            FooterControls = [new FormControl { Id = "footer_field", Control = "SingleLineText", CustomControlId = "{00000000-0000-0000-0000-000000000001}" }],
        };

        var messages = FormControlValidator.Validate(form, EmptyExistingRoot);

        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void Validate_LegacyClassId_ResolvesWithoutViolation()
    {
        var control = new FormControl { Id = "tn_field", ClassId = "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}" };
        Assert.Empty(FormControlValidator.Validate(FormWith(control), EmptyExistingRoot));
    }
}

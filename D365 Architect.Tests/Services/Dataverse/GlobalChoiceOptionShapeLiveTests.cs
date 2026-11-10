using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// What <see cref="GlobalChoiceMetadataJsonBuilderTests"/> can't prove: that Dataverse keeps the empty
/// <c>Description</c> and <c>ExternalValue</c> this tool now sends for an option it creates, so the exported solution XML
/// matches a maker-portal option (<c>ExternalValue=""</c> plus an empty <c>&lt;Descriptions&gt;</c>) rather than dropping
/// them (#79). Covers both creation paths: an option in a brand-new choice's create body, and one added later through
/// <c>InsertOptionValue</c>. Live test: skipped unless <see cref="LiveDataverse.Settings"/> is set (see docs/DEVELOPING.md);
/// the choice and solution it creates are deleted in <see cref="DisposeAsync"/>, pass or fail.
/// </summary>
[Trait("Category", "Live")]
[Collection(LiveCollection.Name)]
public sealed class GlobalChoiceOptionShapeLiveTests : IAsyncLifetime
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(10);

    private string? _choiceToDelete;
    private Guid? _solutionToDelete;
    private LiveDataverse? _live;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_live is null)
        {
            return;
        }

        var failures = new List<Exception>();
        if (_choiceToDelete is { } choice)
        {
            try
            {
                await _live.DeleteGlobalOptionSetAsync(choice, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (_solutionToDelete is { } solutionId)
        {
            try
            {
                await _live.DeleteSolutionAsync(solutionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        _live.Dispose();
        if (failures.Count > 0)
        {
            throw new AggregateException("Live test clean-up failed; delete the leftover <prefix>_lt* components by hand.", failures);
        }
    }

    [LiveFact]
    public async Task CreatedAndInsertedOptions_ExportWithEmptyDescriptionAndExternalValue()
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var ct = timeout.Token;
        _live = await LiveDataverse.ConnectAsync(ct);
        var solution = _live.NewName();
        _solutionToDelete = await _live.CreateSolutionAsync(solution, ct);
        var name = _live.NewName("choice");
        _choiceToDelete = name;

        // Path 1: an option in a brand-new choice's create body.
        var created = new AttributeOptionDefinition { Value = 100000000, Label = "Created with the choice" };
        var createBody = GlobalChoiceMetadataJsonBuilder.BuildCreateBody(new GlobalChoiceDefinition { Name = name, DisplayName = name, Options = [created] });
        await _live.Client.CreateGlobalOptionSetAsync(_live.EnvironmentUrl, _live.AccessToken, createBody, solution, ct);

        // Path 2: an option added to the existing choice through InsertOptionValue.
        var inserted = new AttributeOptionDefinition { Value = 100000001, Label = "Inserted later" };
        var plan = Assert.Single(GlobalChoiceMetadataJsonBuilder.BuildOptionChangePlans(
            new GlobalChoiceDefinition { Name = name, Options = [created, inserted] },
            new GlobalChoiceDefinition { Name = name, Options = [created] }));
        Assert.Equal(OptionChangeAction.InsertOption, plan.Action);
        await _live.Client.InsertOptionValueAsync(_live.EnvironmentUrl, _live.AccessToken, plan.RequestBody, ct);
        await _live.Client.PublishGlobalOptionSetsAsync(_live.EnvironmentUrl, _live.AccessToken, [name], ct);

        var customizations = await _live.ExportCustomizationsXmlAsync(solution, ct);

        var optionSet = customizations.Descendants("optionset").SingleOrDefault(o => (string?)o.Attribute("Name") == name);
        Expect.True(optionSet is not null, $"The exported customizations.xml should contain optionset '{name}'.");
        foreach (var option in new[] { created, inserted })
        {
            var element = optionSet!.Element("options")!.Elements("option").Single(o => (int)o.Attribute("value")! == option.Value);
            Expect.Equal("", (string?)element.Attribute("ExternalValue"), $"Option {option.Value} ('{option.Label}') should export ExternalValue=\"\". Got: {element}");
            var description = element.Element("Descriptions")?.Elements("Description").SingleOrDefault(d => (string?)d.Attribute("languagecode") == "1033");
            Expect.Equal("", (string?)description?.Attribute("description"), $"Option {option.Value} ('{option.Label}') should export an empty 1033 <Description>. Got: {element}");
        }
    }
}

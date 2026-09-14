using System.Text.Json.Nodes;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// Covers <see cref="OptionSetDiffer"/> — the match-by-Value insert/rename/
/// reorder algorithm shared by a column's own local options and a global
/// choice's own options.
/// </summary>
public sealed class OptionSetDifferTests
{
    private static AttributeOptionDefinition Option(int value, string label) => new() { Value = value, Label = label };

    private static IReadOnlyList<OptionChangePlan> Diff(IReadOnlyList<AttributeOptionDefinition> local, IReadOnlyList<AttributeOptionDefinition>? existing) =>
        OptionSetDiffer.Diff(
            local,
            existing,
            option => new OptionChangePlan(OptionChangeAction.InsertOption, new JsonObject { ["Value"] = option.Value, ["Label"] = option.Label }, $"insert {option.Value}"),
            (value, label) => new OptionChangePlan(OptionChangeAction.UpdateOption, new JsonObject { ["Value"] = value, ["Label"] = label }, $"rename {value}"),
            values => new OptionChangePlan(OptionChangeAction.OrderOptions, new JsonObject { ["Values"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) }, "reorder"));

    [Fact]
    public void Diff_IdenticalOptionsInSameOrder_ProducesNoPlans()
    {
        var options = new[] { Option(1, "Red"), Option(2, "Blue") };
        Assert.Empty(Diff(options, options));
    }

    [Fact]
    public void Diff_NewLocalValue_ProducesInsertPlan()
    {
        var existing = new[] { Option(1, "Red") };
        var local = new[] { Option(1, "Red"), Option(2, "Blue") };

        var plan = Assert.Single(Diff(local, existing));

        Assert.Equal(OptionChangeAction.InsertOption, plan.Action);
        Assert.Equal(2, (int)plan.RequestBody["Value"]!);
    }

    [Fact]
    public void Diff_ExistingValueWithDifferentLabel_ProducesUpdatePlan()
    {
        var existing = new[] { Option(1, "Red") };
        var local = new[] { Option(1, "Crimson") };

        var plan = Assert.Single(Diff(local, existing));

        Assert.Equal(OptionChangeAction.UpdateOption, plan.Action);
        Assert.Equal("Crimson", (string)plan.RequestBody["Label"]!);
    }

    [Fact]
    public void Diff_SameValuesReordered_ProducesOneOrderPlan()
    {
        var existing = new[] { Option(1, "Red"), Option(2, "Blue") };
        var local = new[] { Option(2, "Blue"), Option(1, "Red") };

        var plan = Assert.Single(Diff(local, existing));

        Assert.Equal(OptionChangeAction.OrderOptions, plan.Action);
        var values = plan.RequestBody["Values"]!.AsArray().Select(v => (int)v!).ToList();
        Assert.Equal([2, 1], values);
    }

    [Fact]
    public void Diff_ExistingValueMissingFromLocal_IsNeverDeleted()
    {
        // No DeleteOption delegate exists at all - an option present live but
        // absent from local never produces a plan of any kind.
        var existing = new[] { Option(1, "Red"), Option(2, "Blue") };
        var local = new[] { Option(1, "Red") };

        Assert.Empty(Diff(local, existing));
    }

    [Fact]
    public void Diff_InsertAndReorderTogether_OnlyReportsTheInsert()
    {
        // Reorder is only ever detected when the value SET is otherwise
        // identical (plans.Count == 0 after the insert/rename pass) - an
        // insert alongside a reorder never also emits a reorder plan.
        var existing = new[] { Option(1, "Red"), Option(2, "Blue") };
        var local = new[] { Option(3, "Green"), Option(2, "Blue"), Option(1, "Red") };

        var plan = Assert.Single(Diff(local, existing));
        Assert.Equal(OptionChangeAction.InsertOption, plan.Action);
    }

    [Fact]
    public void Diff_NullExistingOptions_TreatsEveryLocalValueAsAnInsert()
    {
        var local = new[] { Option(1, "Red"), Option(2, "Blue") };

        var plans = Diff(local, existing: null);

        Assert.Equal(2, plans.Count);
        Assert.All(plans, p => Assert.Equal(OptionChangeAction.InsertOption, p.Action));
    }

    [Fact]
    public void Diff_EmptyLocalAndExisting_ProducesNoPlans()
    {
        Assert.Empty(Diff([], []));
    }
}

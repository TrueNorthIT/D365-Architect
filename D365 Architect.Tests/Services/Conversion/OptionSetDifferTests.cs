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
    public void Diff_InsertAndReorderTogether_ReportsBoth()
    {
        // Regression (round 3): reorder used to only ever be detected when
        // the value set was otherwise identical (plans.Count == 0 after the
        // insert/rename pass), so a genuine reorder requested alongside an
        // insert was silently dropped. Dataverse's own InsertOptionValue has
        // no position parameter this tool sets (a new option always lands
        // at the end), so the predicted post-insert order here is [1,2,3] -
        // still not [3,2,1], so a reorder plan naming the full local order
        // is still expected on top of the insert.
        var existing = new[] { Option(1, "Red"), Option(2, "Blue") };
        var local = new[] { Option(3, "Green"), Option(2, "Blue"), Option(1, "Red") };

        var plans = Diff(local, existing);

        Assert.Equal(2, plans.Count);
        var insert = Assert.Single(plans, p => p.Action == OptionChangeAction.InsertOption);
        Assert.Equal(3, (int)insert.RequestBody["Value"]!);
        var order = Assert.Single(plans, p => p.Action == OptionChangeAction.OrderOptions);
        var values = order.RequestBody["Values"]!.AsArray().Select(v => (int)v!).ToList();
        Assert.Equal([3, 2, 1], values);
    }

    [Fact]
    public void Diff_InsertAppendedAtTheEndAlreadyMatchesDesiredOrder_NoReorderPlan()
    {
        // The insert alone already produces the desired final order (new
        // value wanted last, and it lands last by default), so no separate
        // OrderOptions call is needed on top of the insert.
        var existing = new[] { Option(1, "Red"), Option(2, "Blue") };
        var local = new[] { Option(1, "Red"), Option(2, "Blue"), Option(3, "Green") };

        var plan = Assert.Single(Diff(local, existing));
        Assert.Equal(OptionChangeAction.InsertOption, plan.Action);
    }

    [Fact]
    public void Diff_RenameAndReorderTogether_ReportsBoth()
    {
        var existing = new[] { Option(1, "Red"), Option(2, "Blue") };
        var local = new[] { Option(2, "Blue"), Option(1, "Crimson") };

        var plans = Diff(local, existing);

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Action == OptionChangeAction.UpdateOption);
        Assert.Contains(plans, p => p.Action == OptionChangeAction.OrderOptions);
    }

    [Fact]
    public void Diff_ReorderRequestedButAnExistingValueIsMissingFromLocal_SkipsReorder()
    {
        // Never attempted when an existing value is unmanaged (absent from
        // local, i.e. a "would remove" this tool never touches) - building a
        // complete Values array for Dataverse's OrderOption action would
        // mean guessing what omitting that value does, which this tool
        // never does.
        var existing = new[] { Option(1, "Red"), Option(2, "Blue"), Option(3, "Green") };
        var local = new[] { Option(2, "Blue"), Option(1, "Red") };

        Assert.Empty(Diff(local, existing));
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

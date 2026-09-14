using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Dataverse;

/// <summary>
/// Covers <see cref="AttributeChangeValidator"/>, including the confirmed-live
/// regressions from this session: the BigInt update refusal (round 12) and
/// the State-vs-Status "no live match" warning fix (round 1).
/// </summary>
public sealed class AttributeChangeValidatorTests
{
    private static AttributeDefinition Attr(string type, string? schemaName = "tn_Test", string name = "tn_test", Action<AttributeDefinitionBuilder>? configure = null)
    {
        var builder = new AttributeDefinitionBuilder(type, schemaName) { Name = name };
        configure?.Invoke(builder);
        return builder.Build();
    }

    // ---- ValidateCreate ----

    [Fact]
    public void ValidateCreate_NoSchemaName_Fails()
    {
        Assert.NotNull(AttributeChangeValidator.ValidateCreate(Attr("String", schemaName: null)));
    }

    [Theory]
    [InlineData("tn_bank name")] // space
    [InlineData("tn-Bank")] // dash
    [InlineData("NoUnderscore")] // no publisher prefix
    [InlineData("_LeadingUnderscore")]
    public void ValidateCreate_InvalidSchemaNamePattern_Fails(string schemaName)
    {
        Assert.NotNull(AttributeChangeValidator.ValidateCreate(Attr("String", schemaName: schemaName, name: schemaName.ToLowerInvariant())));
    }

    [Fact]
    public void ValidateCreate_NameNotDerivedFromSchemaName_Fails()
    {
        var attribute = Attr("String", schemaName: "tn_BankName", name: "tn_wrongname");
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("tn_bankname", error);
    }

    [Fact]
    public void ValidateCreate_ValidStringColumn_Succeeds()
    {
        var attribute = Attr("String", schemaName: "tn_BankName", name: "tn_bankname");
        Assert.Null(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Fact]
    public void ValidateCreate_PicklistWithNoOptionsOrGlobalChoice_Fails()
    {
        var attribute = Attr("Picklist", schemaName: "tn_Choice", name: "tn_choice");
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("Options", error);
    }

    [Fact]
    public void ValidateCreate_PicklistWithBothOptionsAndGlobalChoice_Fails()
    {
        var attribute = Attr("Picklist", schemaName: "tn_Choice", name: "tn_choice", configure: b =>
        {
            b.Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }];
            b.GlobalOptionSetName = "tn_shared";
        });
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("both", error);
    }

    [Fact]
    public void ValidateCreate_PicklistWithDuplicateOptionValues_Fails()
    {
        var attribute = Attr("Picklist", schemaName: "tn_Choice", name: "tn_choice", configure: b => b.Options =
        [
            new AttributeOptionDefinition { Value = 1, Label = "A" },
            new AttributeOptionDefinition { Value = 1, Label = "B" },
        ]);
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("duplicate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCreate_PicklistWithGlobalOptionSetNameOnly_Succeeds()
    {
        var attribute = Attr("Picklist", schemaName: "tn_Choice", name: "tn_choice", configure: b => b.GlobalOptionSetName = "tn_shared");
        Assert.Null(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Fact]
    public void ValidateCreate_LookupWithNoRelationshipSchemaName_Fails()
    {
        var attribute = Attr("Lookup", schemaName: "tn_Contact", name: "tn_contact", configure: b => b.Targets = ["contact"]);
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("RelationshipSchemaName", error);
    }

    [Fact]
    public void ValidateCreate_LookupWithMultipleTargets_Fails()
    {
        var attribute = Attr("Lookup", schemaName: "tn_Contact", name: "tn_contact", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact", "account"];
        });
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("exactly one", error);
    }

    [Fact]
    public void ValidateCreate_ValidLookup_Succeeds()
    {
        var attribute = Attr("Lookup", schemaName: "tn_Contact", name: "tn_contact", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
        });
        Assert.Null(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Theory]
    [InlineData("Parental")]
    [InlineData("ReferentialRestrictDelete")]
    [InlineData("referential")]
    public void ValidateCreate_LookupWithValidRelationshipBehavior_Succeeds(string behavior)
    {
        var attribute = Attr("Lookup", schemaName: "tn_Contact", name: "tn_contact", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
            b.RelationshipBehavior = behavior;
        });
        Assert.Null(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Fact]
    public void ValidateCreate_LookupWithInvalidRelationshipBehavior_Fails()
    {
        var attribute = Attr("Lookup", schemaName: "tn_Contact", name: "tn_contact", configure: b =>
        {
            b.RelationshipSchemaName = "tn_test_contact";
            b.Targets = ["contact"];
            b.RelationshipBehavior = "NotARealBehavior";
        });
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("RelationshipBehavior", error);
    }

    [Fact]
    public void ValidateCreate_CustomerWithWrongTargets_Fails()
    {
        var attribute = Attr("Customer", schemaName: "tn_Customer", name: "tn_customer", configure: b => b.Targets = ["contact"]);
        var error = AttributeChangeValidator.ValidateCreate(attribute);
        Assert.NotNull(error);
        Assert.Contains("Targets", error);
    }

    [Fact]
    public void ValidateCreate_CustomerWithCorrectTargets_Succeeds()
    {
        var attribute = Attr("Customer", schemaName: "tn_Customer", name: "tn_customer", configure: b => b.Targets = ["account", "contact"]);
        Assert.Null(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateCreate_StringWithNonPositiveMaxLength_Fails(int maxLength)
    {
        var attribute = Attr("String", schemaName: "tn_Field", name: "tn_field", configure: b => b.MaxLength = maxLength);
        Assert.NotNull(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Fact]
    public void ValidateCreate_IntegerMinValueOutsideRange_Fails()
    {
        var attribute = Attr("Integer", schemaName: "tn_Field", name: "tn_field", configure: b => b.MinValue = -3000000000.0);
        Assert.NotNull(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Fact]
    public void ValidateCreate_MinValueGreaterThanMaxValue_Fails()
    {
        var attribute = Attr("Integer", schemaName: "tn_Field", name: "tn_field", configure: b => { b.MinValue = 100; b.MaxValue = 1; });
        Assert.NotNull(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void ValidateCreate_DecimalPrecisionOutsideRange_Fails(int precision)
    {
        var attribute = Attr("Decimal", schemaName: "tn_Field", name: "tn_field", configure: b => b.Precision = precision);
        Assert.NotNull(AttributeChangeValidator.ValidateCreate(attribute));
    }

    [Fact]
    public void ValidateCreate_InvalidRequiredLevel_Fails()
    {
        var attribute = Attr("String", schemaName: "tn_Field", name: "tn_field", configure: b => b.RequiredLevel = "NotARealLevel");
        Assert.NotNull(AttributeChangeValidator.ValidateCreate(attribute));
    }

    // ---- ValidateUpdate: BigInt refusal (round 12 regression) ----

    [Fact]
    public void ValidateUpdate_BigInt_AlwaysFails()
    {
        var local = Attr("BigInt", configure: b => b.Description = "new description");
        var existing = Attr("BigInt");

        var error = AttributeChangeValidator.ValidateUpdate(local, existing);

        Assert.NotNull(error);
        Assert.Contains("BigInt", error);
    }

    [Fact]
    public void ValidateCreate_BigInt_IsUnaffectedByTheUpdateRefusal()
    {
        // ValidateCreate never passes existing, so the BigInt guard (which
        // only fires when existing is not null) must never block a create.
        var attribute = Attr("BigInt", schemaName: "tn_Counter", name: "tn_counter");
        Assert.Null(AttributeChangeValidator.ValidateCreate(attribute));
    }

    // ---- ValidateUpdate: global/local Picklist switch guard ----

    [Fact]
    public void ValidateUpdate_SwitchingBetweenTwoDifferentGlobalChoices_Fails()
    {
        var local = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_choiceB");
        var existing = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_choiceA");

        Assert.NotNull(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    [Fact]
    public void ValidateUpdate_SwitchingFromLocalToGlobal_Fails()
    {
        var local = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_choice");
        var existing = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }]);

        Assert.NotNull(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    [Fact]
    public void ValidateUpdate_SwitchingFromGlobalToLocal_Fails()
    {
        var local = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "A" }]);
        var existing = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_choice");

        Assert.NotNull(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    [Fact]
    public void ValidateUpdate_SameGlobalChoiceBothSides_Succeeds()
    {
        var local = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_choice");
        var existing = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_choice");

        Assert.Null(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    [Fact]
    public void ValidateUpdate_LocalOptionsBothSides_Succeeds()
    {
        var local = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Renamed" }]);
        var existing = Attr("Picklist", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Original" }]);

        Assert.Null(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    [Fact]
    public void ValidateUpdate_NullFieldsOnLocal_NeverBlock()
    {
        // local specifies nothing at all for a Picklist already globally
        // bound - null means "don't touch," never treated as a switch.
        var local = Attr("Picklist", configure: b => b.Description = "unrelated change");
        var existing = Attr("Picklist", configure: b => b.GlobalOptionSetName = "tn_choice");

        Assert.Null(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    [Fact]
    public void ValidateUpdate_PrecisionAlreadyOutOfRangeButUnchanged_NeverBlocks()
    {
        // Confirmed live against a real table's own out-of-range Precision:
        // re-sending the same value while updating something else must never
        // be treated as a new violation.
        var local = Attr("Decimal", configure: b => { b.Precision = 12; b.Description = "unrelated change"; });
        var existing = Attr("Decimal", configure: b => b.Precision = 12);

        Assert.Null(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    [Fact]
    public void ValidateUpdate_PrecisionChangedToADifferentOutOfRangeValue_Blocks()
    {
        var local = Attr("Decimal", configure: b => b.Precision = 15);
        var existing = Attr("Decimal", configure: b => b.Precision = 12);

        Assert.NotNull(AttributeChangeValidator.ValidateUpdate(local, existing));
    }

    // ---- Warnings: State vs Status label-fallback fix (round 1 regression) ----

    [Fact]
    public void Warnings_State_ValueMismatch_WarnsEvenWhenLabelCoincidentallyMatches()
    {
        // A State option's Value is never server-assigned/placeholder, so a
        // label coincidence must never excuse a genuine Value mismatch.
        var local = Attr("State", configure: b => b.Options = [new AttributeOptionDefinition { Value = 99, Label = "Active" }]);
        var existing = Attr("State", configure: b => b.Options = [new AttributeOptionDefinition { Value = 0, Label = "Active" }]);

        var warnings = AttributeChangeValidator.Warnings(local, existing);

        Assert.Contains(warnings, w => w.Contains("no live match"));
    }

    [Fact]
    public void Warnings_Status_ValueMismatchButLabelMatches_IsSuppressed()
    {
        // Status's own Value is server-assigned; a Label match alone means
        // "already inserted, not yet re-exported" - never warned about.
        var local = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 999999, Label = "Already Live" }]);
        var existing = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 1, Label = "Already Live" }]);

        Assert.Empty(AttributeChangeValidator.Warnings(local, existing));
    }

    [Fact]
    public void Warnings_Status_GenuinelyUnmatchedWithNoState_WarnsAboutMissingState()
    {
        var local = Attr("Status", configure: b => b.Options = [new AttributeOptionDefinition { Value = 999999, Label = "Brand New" }]);
        var existing = Attr("Status", configure: b => b.Options = []);

        var warnings = AttributeChangeValidator.Warnings(local, existing);

        Assert.Contains(warnings, w => w.Contains("no State given"));
    }

    [Fact]
    public void Warnings_GlobalBoundStateOrStatus_NeverWarnsAboutOptions()
    {
        // Out of scope entirely once GlobalOptionSetName is set on the live
        // column - this tool never touches a global choice's own options.
        var local = Attr("State", configure: b => b.Options = [new AttributeOptionDefinition { Value = 99, Label = "Whatever" }]);
        var existing = Attr("State", configure: b => b.GlobalOptionSetName = "tn_test_statecode");

        Assert.Empty(AttributeChangeValidator.Warnings(local, existing));
    }

    [Fact]
    public void Warnings_LoweringMaxLength_Warns()
    {
        var local = Attr("String", configure: b => b.MaxLength = 50);
        var existing = Attr("String", configure: b => b.MaxLength = 100);

        Assert.Contains(AttributeChangeValidator.Warnings(local, existing), w => w.Contains("MaxLength"));
    }

    [Fact]
    public void Warnings_RaisingMaxLength_NeverWarns()
    {
        var local = Attr("String", configure: b => b.MaxLength = 200);
        var existing = Attr("String", configure: b => b.MaxLength = 100);

        Assert.Empty(AttributeChangeValidator.Warnings(local, existing));
    }
}

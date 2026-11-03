using D365Architect.Services.Conversion.Models;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// The table every <c>table create</c> test starts from - the requirements'
/// own example, <c>tn_project</c> with <c>tn_name</c>, <c>tn_budget</c> and
/// <c>tn_startdate</c> - fully valid by default. A test that needs a value
/// missing passes an explicit <c>null</c> for it, e.g. <c>Table(ownershipType: null)</c>.
/// </summary>
public static class TableCreateFixtures
{
    public static AttributeDefinition NameColumn(string? schemaName = "tn_Name", string? format = null, int? maxLength = null) => new()
    {
        Name = "tn_name",
        Type = "String",
        SchemaName = schemaName,
        DisplayName = "Name",
        Format = format,
        MaxLength = maxLength,
    };

    public static AttributeDefinition BudgetColumn() => new() { Name = "tn_budget", Type = "Money", SchemaName = "tn_Budget", DisplayName = "Budget" };

    public static AttributeDefinition StartDateColumn() => new() { Name = "tn_startdate", Type = "DateTime", SchemaName = "tn_StartDate", DisplayName = "Start date" };

    public static EntityDefinition Table(
        string? schemaName = "tn_Project",
        string? displayName = "Project",
        string? pluralDisplayName = "Projects",
        string? description = "Tracks projects",
        string? ownershipType = "UserOwned",
        string? primaryNameAttribute = "tn_name",
        bool? isActivity = null,
        bool? hasActivities = null,
        bool? hasNotes = null,
        IReadOnlyList<AttributeDefinition>? attributes = null) => new()
    {
        LogicalName = "tn_project",
        SchemaName = schemaName,
        DisplayName = displayName,
        PluralDisplayName = pluralDisplayName,
        Description = description,
        OwnershipType = ownershipType,
        PrimaryNameAttribute = primaryNameAttribute,
        IsActivity = isActivity,
        HasActivities = hasActivities,
        HasNotes = hasNotes,
        Attributes = attributes ?? [NameColumn(), BudgetColumn(), StartDateColumn()],
    };

    /// <summary>
    /// The same table as hand-written YAML, as a user would author it. Each
    /// <c>include*</c> flag lets a test drop one required key to prove it is
    /// refused rather than guessed.
    /// </summary>
    public static string TableYaml(bool includeOwnership = true, bool includePrimaryName = true, bool isActivity = false) =>
        string.Join("\n",
            new[]
            {
                "entity: tn_project",
                "schemaName: tn_Project",
                "displayName: Project",
                "pluralDisplayName: Projects",
                "description: Tracks projects",
                includeOwnership ? "ownershipType: UserOwned" : null,
                isActivity ? "isActivity: true" : null,
                includePrimaryName ? "primaryNameAttribute: tn_name" : null,
                "attributes:",
                "- name: tn_name",
                "  type: String",
                "  schemaName: tn_Name",
                "  displayName: Name",
                "- name: tn_budget",
                "  type: Money",
                "  schemaName: tn_Budget",
                "  displayName: Budget",
                "- name: tn_startdate",
                "  type: DateTime",
                "  schemaName: tn_StartDate",
                "  displayName: Start date",
            }.Where(line => line is not null)) + "\n";
}

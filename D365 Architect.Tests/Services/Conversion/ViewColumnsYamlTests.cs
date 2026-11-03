using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// "A view can list its columns with <c>columns:</c> instead
/// of raw <c>layoutXml</c>" and "A column can have a width, otherwise a
/// sensible default". These pin the two YAML shapes an entry may take - a
/// plain string, or <c>{ name, width }</c> - and that they survive a round trip
/// through <see cref="ViewYamlSerializer"/>/<see cref="ViewYamlDeserializer"/>
/// (the deserialiser needs a converter for the string-or-mapping union; the
/// serialiser writes a bare string when there is no width).
/// </summary>
public sealed class ViewColumnsYamlTests
{
    // Example: `columns: [bogdan_name, createdon]`. Each plain string is a column with no explicit width.
    // Code path: ViewYamlDeserializer.FromYaml -> ViewDefinition.Columns.
    [Fact]
    public void FromYaml_ColumnsAsPlainStrings_AreReadAsColumnsWithoutAWidth()
    {
        const string yaml = """
            view: All Invoice Centres
            entity: bogdan_tablecreatetest
            columns:
            - bogdan_name
            - createdon
            """;

        var view = ViewYamlDeserializer.FromYaml(yaml);

        Expect.Sequence(["bogdan_name", "createdon"], view.Columns?.Select(c => c.Name).ToList() ?? [], "'columns' entries written as plain strings should be read, in order, as column names.");
        Expect.True(view.Columns!.All(c => c.Width is null), "a column written as a plain string has no explicit width (the default of 150 is applied on import).");
    }

    // Example: `- name: bogdan_name, width: 300`.
    [Fact]
    public void FromYaml_ColumnAsNameAndWidthMapping_CarriesTheWidth()
    {
        const string yaml = """
            view: All Invoice Centres
            entity: bogdan_tablecreatetest
            columns:
            - name: bogdan_name
              width: 300
            - name: createdon
              width: 125
            """;

        var view = ViewYamlDeserializer.FromYaml(yaml);

        Expect.Sequence(["bogdan_name", "createdon"], view.Columns?.Select(c => c.Name).ToList() ?? [], "'name'/'width' entries should be read, in order, as column names.");
        Expect.Sequence([300, 125], view.Columns!.Select(c => c.Width ?? -1).ToList(), "each mapping's 'width' should be carried onto the column.");
    }

    // Both shapes may be mixed in one list (width only where it differs from the default).
    [Fact]
    public void FromYaml_StringsAndMappingsMixedInOneList_AreBothRead()
    {
        const string yaml = """
            view: All Invoice Centres
            entity: bogdan_tablecreatetest
            columns:
            - name: bogdan_name
              width: 300
            - createdon
            """;

        var view = ViewYamlDeserializer.FromYaml(yaml);

        Expect.Sequence(["bogdan_name", "createdon"], view.Columns?.Select(c => c.Name).ToList() ?? [], "a list mixing both shapes should keep every column, in order.");
        Expect.Equal(300, view.Columns![0].Width ?? -1, "the mapping entry keeps its width.");
        Expect.Equal(null, view.Columns[1].Width, "the plain-string entry has no explicit width.");
    }

    // Absent key means "no columns given" (the raw layoutXml path), never an empty list.
    [Fact]
    public void FromYaml_NoColumnsKey_LeavesColumnsNull()
    {
        const string yaml = """
            view: All Invoice Centres
            entity: bogdan_tablecreatetest
            layoutXml: <grid name="resultset" object="10722" />
            """;

        var view = ViewYamlDeserializer.FromYaml(yaml);

        Expect.True(view.Columns is null, "a view file without 'columns' (an exported one, say) should read with Columns null, so import takes the raw layoutXml path.");
    }

    // Round trip is the contract (modify-exports-imports skill): width survives, and a width-less column comes back width-less.
    [Fact]
    public void ToYamlThenFromYaml_PreservesColumnsAndTheirWidths()
    {
        var original = ViewFormCreateFixtures.ViewOf(columns: [ViewFormCreateFixtures.Col("bogdan_name", 300), ViewFormCreateFixtures.Col("createdon")]);

        var roundTripped = ViewYamlDeserializer.FromYaml(ViewYamlSerializer.ToYaml(original));

        Expect.Sequence(["bogdan_name", "createdon"], roundTripped.Columns?.Select(c => c.Name).ToList() ?? [], "columns should survive a YAML round trip in order.");
        Expect.Sequence([300, -1], roundTripped.Columns!.Select(c => c.Width ?? -1).ToList(), "a width should survive the round trip and an absent width should stay absent.");
    }

    // Minimal YAML (modify-exports-imports skill): no width means a bare string, not `name: x` with a null width.
    [Fact]
    public void ToYaml_ColumnWithoutWidth_IsWrittenAsAPlainString()
    {
        var yaml = ViewYamlSerializer.ToYaml(ViewFormCreateFixtures.ViewOf(columns: [ViewFormCreateFixtures.Col("createdon")]));

        Expect.True(yaml.Split('\n').Any(line => line.TrimEnd() == "- createdon"), $"a column with no width should serialise as the plain string '- createdon'. YAML was:\n{yaml}");
    }

    [Fact]
    public void ToYaml_ColumnWithWidth_IsWrittenAsANameAndWidthMapping()
    {
        var yaml = ViewYamlSerializer.ToYaml(ViewFormCreateFixtures.ViewOf(columns: [ViewFormCreateFixtures.Col("bogdan_name", 300)]));

        Expect.Contains("name: bogdan_name", yaml, "a column with a width should serialise as a mapping with 'name'.");
        Expect.Contains("width: 300", yaml, "and with its 'width'.");
    }

    // Omit-when-absent, as for every other optional key.
    [Fact]
    public void ToYaml_NoColumns_OmitsTheKey()
    {
        var yaml = ViewYamlSerializer.ToYaml(ViewFormCreateFixtures.ViewOf(layoutXml: ViewFormCreateFixtures.ExportedLayoutXml()));

        Assert.DoesNotContain("columns", yaml, StringComparison.Ordinal);
    }
}

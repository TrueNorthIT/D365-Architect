using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;
using D365Architect.Tests.TestSupport;
using Xunit;
using static D365Architect.Tests.TestSupport.ViewFormCreateFixtures;

namespace D365Architect.Tests.Services.Conversion;

/// <summary>
/// How <see cref="ViewImportService.PreviewAsync"/> turns a
/// view's <c>columns:</c> into the layout and FetchXML it will write, fills in
/// the table's type code, and leaves a raw <c>layoutXml</c> (an exported view
/// file) alone. Every test goes through <c>PreviewAsync</c> against a
/// <see cref="FakeDataverseClient"/> whose live table is the one in
/// <see cref="ViewFormCreateFixtures"/> (type code 10722), and reads
/// <c>NewLayoutXml</c>/<c>NewFetchXml</c> off the preview; layouts are compared
/// as parsed XML so the tests don't pin whitespace.
///
/// Decisions pinned here (see the hand-over report): the grid's <c>jump</c> is
/// the table's live PrimaryNameAttribute and its <c>row id</c> the live
/// PrimaryIdAttribute (both from <c>IDataverseClient.TryGetEntityIdentityAsync</c>);
/// the live type code always wins over an <c>object</c> already in a raw layout;
/// a table that doesn't exist, an empty or duplicated <c>columns</c> list, and
/// <c>columns</c> together with <c>layoutXml</c> are refused with an
/// <see cref="InvalidDataException"/> (the exception <c>table import</c> already
/// uses for a bad file).
/// </summary>
public sealed class ViewImportServiceColumnsTests
{
    private static Task<ViewImportPreview> Preview(FakeDataverseClient client, ViewDefinition view) =>
        new ViewImportService(client).PreviewAsync(EnvironmentUrl, "token", view, CancellationToken.None);

    /// <summary>A live view to update, so the preview is an update rather than a create.</summary>
    private static FakeDataverseClient ClientWithLiveView(string? liveFetch = FetchXmlWithOnlyAnOrder, string? liveLayout = null)
    {
        var client = ClientWithTable();
        client.SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), null, liveFetch, liveLayout);
        return client;
    }

    // ---- the grid built from columns ----

    // Example: `columns: [bogdan_name, createdon]` -> a grid with those two columns.
    // An unspecified width is 150.
    // Code path: ViewImportService.PreviewAsync building ViewImportPreview.NewLayoutXml.
    [Fact]
    public async Task PreviewAsync_ColumnsAsPlainNames_BuildsGridWithThoseColumnsInOrderAtDefaultWidth150()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name"), Col("createdon")], fetchXml: FetchXmlWithOnlyAnOrder));

        var grid = Grid(preview.NewLayoutXml ?? throw new Xunit.Sdk.XunitException("columns should produce a NewLayoutXml, but it was null."));
        Expect.Sequence([("bogdan_name", "150"), ("createdon", "150")], grid.Cells, "the grid should list the columns in the order given, each at the default width of 150.");
    }

    // Example: `- name: bogdan_name, width: 300`; the other column keeps the default.
    [Fact]
    public async Task PreviewAsync_ColumnWithAWidth_UsesThatWidthAndOthersKeepTheDefault()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name", 300), Col("createdon")], fetchXml: FetchXmlWithOnlyAnOrder));

        var grid = Grid(preview.NewLayoutXml ?? "<grid/>");
        Expect.Sequence([("bogdan_name", "300"), ("createdon", "150")], grid.Cells, "a column's explicit width should be used and an unspecified one should be 150.");
    }

    // The generated grid matches a real exported one: name/select/icon/preview as Dataverse writes them,
    // jump = the table's primary name column, row id = its primary id column. Here the primary name isn't
    // even among the columns listed, so 'jump' can't just be "the first column".
    [Fact]
    public async Task PreviewAsync_Columns_GridJumpsToThePrimaryNameAndRowIdIsThePrimaryId()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("createdon")], fetchXml: FetchXmlWithOnlyAnOrder));

        var grid = Grid(preview.NewLayoutXml ?? "<grid/>");
        Expect.Equal("resultset", grid.GridAttributes.GetValueOrDefault("name"), "the grid's name should be 'resultset', as in an exported layout.");
        Expect.Equal(NameColumn, grid.GridAttributes.GetValueOrDefault("jump"), "'jump' should be the table's primary name attribute (from its live metadata), even when that column isn't listed.");
        Expect.Equal(IdColumn, grid.RowId, "the row's 'id' should be the table's primary id attribute (from its live metadata).");
        Expect.Equal("1", grid.GridAttributes.GetValueOrDefault("select"), "'select' should be 1, as in an exported layout.");
        Expect.Equal("1", grid.GridAttributes.GetValueOrDefault("icon"), "'icon' should be 1, as in an exported layout.");
        Expect.Equal("1", grid.GridAttributes.GetValueOrDefault("preview"), "'preview' should be 1, as in an exported layout.");
    }

    // Example: "A view for a brand-new table works without knowing its type code."
    [Fact]
    public async Task PreviewAsync_Columns_SetsObjectToTheTablesLiveTypeCode()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name")], fetchXml: FetchXmlWithOnlyAnOrder));

        var grid = Grid(preview.NewLayoutXml ?? "<grid/>");
        Expect.Equal(LiveTypeCode.ToString(), grid.GridAttributes.GetValueOrDefault("object"), "the grid's 'object' should be filled in from the table's live type code; the YAML never says it.");
    }

    // ---- FetchXML gets the columns ----

    // Example: "FetchXML only has <order>, yet the saved view queries every listed column."
    [Fact]
    public async Task PreviewAsync_Columns_AddsListedColumnsMissingFromTheFetchXml()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name"), Col("createdon")], fetchXml: FetchXmlWithOnlyAnOrder));

        var attributes = FetchAttributes(preview.NewFetchXml ?? "<fetch><entity name=\"x\"/></fetch>");
        Expect.True(attributes.Contains("bogdan_name") && attributes.Contains("createdon"), $"every listed column should be queried by the saved view's FetchXML. Attributes found: <{string.Join(", ", attributes)}>.");
    }

    // "the table's id column is always included".
    [Fact]
    public async Task PreviewAsync_Columns_AlwaysIncludesTheIdColumnInTheFetchXml()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name")], fetchXml: FetchXmlWithOnlyAnOrder));

        var attributes = FetchAttributes(preview.NewFetchXml ?? "<fetch><entity name=\"x\"/></fetch>");
        Expect.True(attributes.Contains(IdColumn), $"the table's primary id column ('{IdColumn}') should always be queried, even though it isn't listed. Attributes found: <{string.Join(", ", attributes)}>.");
    }

    // A column the FetchXML already has isn't added a second time.
    [Fact]
    public async Task PreviewAsync_Columns_NeverDuplicatesAnAttributeTheFetchXmlAlreadyHas()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name"), Col("createdon")], fetchXml: ExportedFetchXml));

        var attributes = FetchAttributes(preview.NewFetchXml ?? "<fetch><entity name=\"x\"/></fetch>");
        Expect.Sequence(attributes.Distinct().OrderBy(a => a).ToList(), attributes.OrderBy(a => a).ToList(), $"no attribute should appear twice. Attributes found: <{string.Join(", ", attributes)}>.");
    }

    // Only attributes are added: the sort and the filter the author wrote stay exactly as written.
    [Fact]
    public async Task PreviewAsync_Columns_LeavesTheFetchXmlsOrderAndFilterUntouched()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name"), Col("createdon")], fetchXml: ExportedFetchXml));

        var entity = XElement.Parse(preview.NewFetchXml ?? "<fetch><entity name=\"x\"/></fetch>").Element("entity")!;
        var original = XElement.Parse(ExportedFetchXml).Element("entity")!;
        Expect.Equal(original.Element("order")?.ToString(), entity.Element("order")?.ToString(), "the FetchXML's <order> should be untouched.");
        Expect.Equal(original.Element("filter")?.ToString(), entity.Element("filter")?.ToString(), "the FetchXML's <filter> should be untouched.");
    }

    // On an update with columns but no fetchXml in the YAML, the live FetchXML is the base, so the saved view still queries every column.
    // (Otherwise "no fetchXml" would mean "don't touch", and the new columns would show blank.)
    [Fact]
    public async Task PreviewAsync_ColumnsOnAnExistingViewWithNoFetchXmlInTheYaml_AddsTheColumnsToTheLiveFetchXml()
    {
        var client = ClientWithLiveView(liveFetch: FetchXmlWithOnlyAnOrder);

        var preview = await Preview(client, ViewOf(columns: [Col("bogdan_name"), Col("createdon")]));

        var attributes = FetchAttributes(preview.NewFetchXml ?? "<fetch><entity name=\"x\"/></fetch>");
        Expect.True(attributes.Contains("bogdan_name") && attributes.Contains("createdon") && attributes.Contains(IdColumn),
            $"with no FetchXML in the YAML the live one should be the base, and gain the listed columns and the id column. Attributes found: <{string.Join(", ", attributes)}>.");
        Expect.True(XElement.Parse(preview.NewFetchXml!).Element("entity")!.Element("order") is not null, "the live FetchXML's own <order> should be kept.");
    }

    // A brand-new view with columns and no FetchXML at all queries exactly the listed columns on the table (plus the id), with no filter.
    [Fact]
    public async Task PreviewAsync_NewViewWithColumnsAndNoFetchXml_QueriesTheTableForJustThoseColumns()
    {
        var preview = await Preview(ClientWithTable(), ViewOf(columns: [Col("bogdan_name"), Col("createdon")]));

        var fetch = XElement.Parse(preview.NewFetchXml ?? "<fetch><entity name=\"x\"/></fetch>");
        Expect.Equal(TableName, (string?)fetch.Element("entity")?.Attribute("name"), "a generated FetchXML should query the view's own table.");
        var attributes = FetchAttributes(preview.NewFetchXml!);
        Expect.Equal(3, attributes.Distinct().Count(a => a is NameColumn or "createdon" or IdColumn), $"a generated FetchXML should query the listed columns and the id column. Attributes found: <{string.Join(", ", attributes)}>.");
    }

    // ---- raw layoutXml ----

    // "for both `columns:` and raw `layoutXml`" - a raw layout with no object gets it.
    [Fact]
    public async Task PreviewAsync_RawLayoutXmlWithoutObject_GetsTheLiveTypeCode()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(fetchXml: ExportedFetchXml, layoutXml: ExportedLayoutXml(objectTypeCode: null)));

        var grid = Grid(preview.NewLayoutXml ?? "<grid/>");
        Expect.Equal(LiveTypeCode.ToString(), grid.GridAttributes.GetValueOrDefault("object"), "a raw layout missing 'object' should have it set from the table's live type code (Dataverse otherwise answers 400 'The required attribute object is missing').");
        Expect.Sequence([("bogdan_name", "300"), ("createdon", "125")], grid.Cells, "only 'object' should be added; the layout's own cells should be untouched.");
        Expect.Equal(NameColumn, grid.GridAttributes.GetValueOrDefault("jump"), "the layout's other attributes should be untouched.");
    }

    // Pinned decision: when the file's object is present but wrong (an export from another environment, or a copy-paste), the live type code wins.
    [Fact]
    public async Task PreviewAsync_RawLayoutXmlWithTheWrongObject_TheLiveTypeCodeWins()
    {
        var preview = await Preview(ClientWithLiveView(), ViewOf(fetchXml: ExportedFetchXml, layoutXml: ExportedLayoutXml(objectTypeCode: 3)));

        var grid = Grid(preview.NewLayoutXml ?? "<grid/>");
        Expect.Equal(LiveTypeCode.ToString(), grid.GridAttributes.GetValueOrDefault("object"), "a wrong 'object' (3) in the file should be replaced by the table's live type code.");
    }

    // "exported view files import unchanged" - a layout whose object is already right goes through byte for byte.
    [Fact]
    public async Task PreviewAsync_RawLayoutXmlWithTheRightObject_IsSentByteForByte()
    {
        var layout = ExportedLayoutXml(LiveTypeCode);

        var preview = await Preview(ClientWithLiveView(), ViewOf(fetchXml: ExportedFetchXml, layoutXml: layout));

        Expect.Equal(layout, preview.NewLayoutXml, "an exported layout whose 'object' already matches should be written back exactly as the file has it.");
    }

    // The raw path never touches the FetchXML either (the augmentation is for columns: only).
    [Fact]
    public async Task PreviewAsync_RawLayoutXml_LeavesTheFetchXmlByteForByte()
    {
        const string fetchWithoutTheLayoutsColumns = FetchXmlWithOnlyAnOrder;

        var preview = await Preview(ClientWithLiveView(), ViewOf(fetchXml: fetchWithoutTheLayoutsColumns, layoutXml: ExportedLayoutXml()));

        Expect.Equal(fetchWithoutTheLayoutsColumns, preview.NewFetchXml, "with a raw layoutXml the FetchXML should be written exactly as the file has it; only 'columns:' triggers adding attributes.");
    }

    // A view with neither columns nor layoutXml (e.g. only a FetchXML tweak) needs nothing from the table, so asks nothing of it.
    [Fact]
    public async Task PreviewAsync_NeitherColumnsNorLayoutXml_MakesNoTableMetadataLookup()
    {
        var client = ClientWithLiveView(liveFetch: "<fetch>old</fetch>");

        await Preview(client, ViewOf(fetchXml: "<fetch>new</fetch>"));

        Expect.Empty(client.EntityIdentityCalls, "an import that has no layout to build or repair shouldn't look the table up (existing view files must import as they do today).");
    }

    // ---- refusals ----

    // Example: Both given -> "use either columns or layoutXml, not both".
    [Fact]
    public async Task PreviewAsync_ColumnsAndLayoutXmlBothGiven_IsRefusedWithAnExplanation()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() =>
            Preview(ClientWithLiveView(), ViewOf(columns: [Col("bogdan_name")], layoutXml: ExportedLayoutXml())));

        Expect.Contains("use either columns or layoutXml, not both", ex.Message, "giving both should be refused with that sentence.");
    }

    // Boundary: `columns: []` isn't a column list; refuse rather than build an empty grid.
    [Fact]
    public async Task PreviewAsync_EmptyColumnsList_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Preview(ClientWithLiveView(), ViewOf(columns: [])));

        Expect.Contains("columns", ex.Message, "an empty 'columns' list should be refused, naming 'columns'.");
    }

    // Boundary: no silent fixes - the same column listed twice is refused, naming it.
    [Fact]
    public async Task PreviewAsync_ColumnListedTwice_IsRefusedNamingIt()
    {
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Preview(ClientWithLiveView(), ViewOf(columns: [Col("createdon"), Col("bogdan_name"), Col("createdon", 90)])));

        Expect.Contains("createdon", ex.Message, "the duplicated column should be named in the refusal.");
    }

    // The type code, name and id attributes can only come from the live table. If it isn't there, say so rather than guess.
    [Fact]
    public async Task PreviewAsync_ColumnsOnATableThatDoesntExist_IsRefusedNamingTheTable()
    {
        var client = new FakeDataverseClient(); // no live table

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Preview(client, ViewOf(columns: [Col("bogdan_name")])));

        Expect.Contains(TableName, ex.Message, "when the table can't be found, the refusal should name it (its type code and primary columns can't be known).");
    }

    // ---- Idempotence: a view built from columns then re-imported reports "no changes" ----

    // The preview of a re-import must compare like with like: feed what the first preview built back in as the live view,
    // and the same file should be a no-op. (Implementation-independent: it doesn't pin the generated text, only its stability.)
    [Fact]
    public async Task PreviewAsync_ColumnsReimportedAgainstTheViewItBuilt_HasNoChanges()
    {
        var view = ViewOf(columns: [Col("bogdan_name", 300), Col("createdon")], fetchXml: FetchXmlWithOnlyAnOrder);
        var first = await Preview(ClientWithLiveView(), view);
        Expect.True(first.NewLayoutXml is not null, "Precondition: the first preview should have built a layout from the columns.");

        var client = ClientWithTable();
        client.SavedQuery = new ExistingSavedQuery(Guid.NewGuid(), null, first.NewFetchXml, first.NewLayoutXml);
        var second = await Preview(client, view);

        Expect.True(!second.HasChanges, "re-importing the same 'columns' file against the view it just built should show no changes.");
    }
}

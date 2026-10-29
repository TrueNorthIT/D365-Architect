using System.Xml.Linq;
using D365Architect.Services.Conversion;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// The shared starting point for the "simpler hand-written forms and views"
/// tests (REQ-22 to REQ-33): a table that has just been created
/// (<c>bogdan_tablecreatetest</c>, type code 10722, the real values from the
/// live check), the exported FetchXML/LayoutXML that environment returned,
/// and small builders for views and forms.
/// </summary>
public static class ViewFormCreateFixtures
{
    public const string TableName = "bogdan_tablecreatetest";
    public const int LiveTypeCode = 10722;
    public const string IdColumn = "bogdan_tablecreatetestid";
    public const string NameColumn = "bogdan_name";

    public static readonly Uri EnvironmentUrl = new("https://test.crm.dynamics.com");

    public static readonly EntityIdentity Identity = new(LiveTypeCode, IdColumn, NameColumn);

    /// <summary>A fake whose live table has the identity above, so a view can be built for it.</summary>
    public static FakeDataverseClient ClientWithTable()
    {
        var client = new FakeDataverseClient();
        client.EntityIdentities[TableName] = Identity;
        return client;
    }

    /// <summary>The FetchXML a view exported from that environment had, filter and all.</summary>
    public const string ExportedFetchXml =
        "<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"bogdan_tablecreatetest\">"
        + "<attribute name=\"bogdan_tablecreatetestid\" /><attribute name=\"bogdan_name\" /><attribute name=\"createdon\" />"
        + "<order attribute=\"bogdan_name\" descending=\"false\" />"
        + "<filter type=\"and\"><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter>"
        + "</entity></fetch>";

    /// <summary>A hand-written FetchXML that only sorts: no columns of its own (REQ-26's example).</summary>
    public const string FetchXmlWithOnlyAnOrder =
        "<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"bogdan_tablecreatetest\">"
        + "<order attribute=\"bogdan_name\" descending=\"false\" />"
        + "</entity></fetch>";

    /// <summary>The LayoutXML a view exported from that environment had, with the given <c>object</c> (null = the attribute left out).</summary>
    public static string ExportedLayoutXml(int? objectTypeCode = LiveTypeCode) =>
        "<grid name=\"resultset\"" + (objectTypeCode is { } code ? $" object=\"{code}\"" : "")
        + " jump=\"bogdan_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"bogdan_tablecreatetestid\">"
        + "<cell name=\"bogdan_name\" width=\"300\" /><cell name=\"createdon\" width=\"125\" /></row></grid>";

    public static ViewColumn Col(string name, int? width = null) => new() { Name = name, Width = width };

    public static ViewDefinition ViewOf(
        string name = "All Invoice Centres",
        IReadOnlyList<ViewColumn>? columns = null,
        string? fetchXml = null,
        string? layoutXml = null,
        Guid? savedQueryId = null,
        string? description = null,
        string? queryType = null,
        string entity = TableName) => new()
    {
        Name = name,
        Entity = entity,
        SavedQueryId = savedQueryId,
        Description = description,
        QueryType = queryType,
        Columns = columns,
        FetchXml = fetchXml,
        LayoutXml = layoutXml,
    };

    /// <summary>What a layout looks like once parsed, so a test can compare structure rather than whitespace.</summary>
    public sealed record ParsedGrid(IReadOnlyDictionary<string, string> GridAttributes, string? RowId, IReadOnlyList<(string Name, string Width)> Cells);

    public static ParsedGrid Grid(string layoutXml)
    {
        var grid = XElement.Parse(layoutXml);
        var row = grid.Element("row");
        return new ParsedGrid(
            grid.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value),
            (string?)row?.Attribute("id"),
            row?.Elements("cell").Select(c => ((string)c.Attribute("name")!, (string?)c.Attribute("width") ?? "")).ToList() ?? []);
    }

    /// <summary>The names of the <c>&lt;attribute&gt;</c> elements directly under the fetch's <c>&lt;entity&gt;</c>, in order.</summary>
    public static IReadOnlyList<string> FetchAttributes(string fetchXml) =>
        XElement.Parse(fetchXml).Element("entity")!.Elements("attribute").Select(a => (string)a.Attribute("name")!).ToList();

    /// <summary>A small, valid main form for the table with one field.</summary>
    public static FormDefinition FormOf(string name = "Invoice Centre", string? type = null, Guid? formId = null, string label = "Name", string entity = TableName) => new()
    {
        Name = name,
        Entity = entity,
        FormId = formId,
        Type = type,
        Tabs =
        [
            new FormTab
            {
                Name = "tab_1",
                Label = "General",
                Columns =
                [
                    new FormColumn
                    {
                        Width = "100%",
                        Sections =
                        [
                            new FormSection
                            {
                                Name = "section_1",
                                Controls = [new FormControl { Id = NameColumn, Field = NameColumn, Label = label, Control = "SingleLineText" }],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    /// <summary>One of the forms Dataverse creates with every new table: all three are named "Information".</summary>
    public static (ExistingSystemForm Form, int Type) InformationForm(Guid id, int type, string entity = TableName) =>
        (new ExistingSystemForm(id, FormXmlWriter.Write(FormOf("Information", label: "Information form")), entity, "Information"), type);

    public const int MainType = 2;
    public const int QuickViewType = 6;
    public const int QuickCreateType = 7;
    public const int CardType = 11;

    public static readonly Guid MainInformationId = new("aaaaaaaa-0000-0000-0000-00000000000a");
    public static readonly Guid CardInformationId = new("aaaaaaaa-0000-0000-0000-00000000000b");
    public static readonly Guid QuickViewInformationId = new("aaaaaaaa-0000-0000-0000-00000000000c");

    /// <summary>The live state of a fresh custom table's forms: main, card and quick view, all named "Information".</summary>
    public static void AddFreshTableForms(FakeDataverseClient client)
    {
        client.LiveSystemForms.Add(InformationForm(MainInformationId, MainType));
        client.LiveSystemForms.Add(InformationForm(CardInformationId, CardType));
        client.LiveSystemForms.Add(InformationForm(QuickViewInformationId, QuickViewType));
    }
}

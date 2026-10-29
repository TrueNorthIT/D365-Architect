using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using D365Architect.Services.Conversion.Models;
using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class ViewImportService(IDataverseClient dataverseClient) : IViewImportService
{
    /// <summary>REQ-25's "sensible default" width, in pixels, for a column the file gives no width.</summary>
    private const int DefaultColumnWidth = 150;

    public async Task<ViewImportPreview> PreviewAsync(Uri environmentUrl, string accessToken, ViewDefinition view, CancellationToken cancellationToken)
    {
        ValidateColumns(view);

        // A savedQueryId in the YAML is authoritative: it disambiguates views
        // sharing a name, and a stale id is a clear error rather than a quiet
        // fall back to the name (which could update the wrong view) or a
        // create (which would hide that the view was deleted). Without one,
        // matching nothing by table + name is a create (REQ-23).
        var existing = view.SavedQueryId is { } savedQueryId
            ? await dataverseClient.TryGetSavedQueryByIdAsync(environmentUrl, accessToken, savedQueryId, cancellationToken)
                ?? throw new ViewNotFoundException(view.Entity, view.Name, savedQueryId)
            : await dataverseClient.TryGetSavedQueryAsync(environmentUrl, accessToken, view.Entity, view.Name, cancellationToken);

        if (existing is null)
        {
            ValidateCreate(view);
        }

        // Only a by-id lookup carries the live table/name (the by-name lookup
        // already filtered on them). Same approach as form import's
        // BuildIdentityMismatchWarning, except a different table is refused
        // outright: it's an id pasted into the wrong file, and writing would
        // overwrite an unrelated view. A name-only difference is a warning,
        // since renaming a view is legitimate.
        if (existing?.EntityLogicalName is not null
            && !string.Equals(existing.EntityLogicalName, view.Entity, StringComparison.OrdinalIgnoreCase))
        {
            throw new ViewIdentityMismatchException(existing.SavedQueryId, view.Entity, view.Name, existing.EntityLogicalName, existing.Name);
        }

        var identityMismatchWarning = existing?.Name is not null && existing.Name != view.Name
            ? $"This YAML's savedQueryId resolves to the view '{existing.Name}' on '{existing.EntityLogicalName}', not this file's own '{view.Name}' — the id is still what's being used (it's authoritative, and the name itself isn't written), but double-check this is the file you meant."
            : null;

        var (newFetchXml, newLayoutXml) = await BuildFetchAndLayoutAsync(environmentUrl, accessToken, view, existing, cancellationToken);

        return new ViewImportPreview(
            existing?.SavedQueryId ?? Guid.Empty,
            existing?.Description,
            view.Description,
            ComparableExistingFetchXml(existing?.FetchXml, newFetchXml),
            newFetchXml,
            existing?.LayoutXml,
            newLayoutXml)
        {
            IdentityMismatchWarning = identityMismatchWarning,
            IsCreate = existing is null,
            Name = view.Name,
            Entity = view.Entity,
        };
    }

    public Task ApplyAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, CancellationToken cancellationToken) =>
        ApplyAsync(environmentUrl, accessToken, preview, solutionUniqueName: null, cancellationToken);

    public Task ApplyAsync(Uri environmentUrl, string accessToken, ViewImportPreview preview, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        if (!preview.IsCreate)
        {
            return dataverseClient.UpdateSavedQueryAsync(
                environmentUrl,
                accessToken,
                preview.SavedQueryId,
                preview.NewDescription,
                preview.NewFetchXml,
                preview.NewLayoutXml,
                solutionUniqueName,
                cancellationToken);
        }

        // Learn, "Customize Views in Model-driven Apps" (Create views):
        // name, returnedtypecode (the table's logical name), fetchxml,
        // layoutxml, and querytype "Must always be zero (0)".
        // https://learn.microsoft.com/power-apps/developer/model-driven-apps/customize-entity-views
        var body = new JsonObject
        {
            ["name"] = preview.Name,
            ["returnedtypecode"] = preview.Entity,
            ["fetchxml"] = preview.NewFetchXml,
            ["layoutxml"] = preview.NewLayoutXml,
            ["querytype"] = 0,
        };

        if (preview.NewDescription is not null)
        {
            body["description"] = preview.NewDescription;
        }

        return dataverseClient.CreateSavedQueryAsync(environmentUrl, accessToken, body, solutionUniqueName, cancellationToken);
    }

    private static readonly Regex SavedQueryIdAttribute = new(@"\s+savedqueryid\s*=\s*(""[^""]*""|'[^']*')");

    /// <summary>
    /// The live FetchXML as the diff should see it. Dataverse stamps
    /// <c>savedqueryid</c> onto the stored <c>&lt;fetch&gt;</c> root when it
    /// saves a view (confirmed live), so a hand-written file without it would
    /// otherwise diff on every run. That attribute is dropped from the live
    /// side only when the file's own <c>&lt;fetch&gt;</c> lacks it; an exported
    /// file carries it (<c>view export</c> writes the live FetchXML verbatim)
    /// and compares as-is. Removed textually so the rest stays byte-for-byte.
    /// </summary>
    private static string? ComparableExistingFetchXml(string? existingFetchXml, string? newFetchXml)
    {
        if (existingFetchXml is null || newFetchXml is null || !existingFetchXml.Contains("savedqueryid", StringComparison.Ordinal))
        {
            return existingFetchXml;
        }

        try
        {
            if (XElement.Parse(existingFetchXml).Attribute("savedqueryid") is null || XElement.Parse(newFetchXml).Attribute("savedqueryid") is not null)
            {
                return existingFetchXml;
            }
        }
        catch (XmlException)
        {
            // Not well-formed: compare as written and let Dataverse have the final word.
            return existingFetchXml;
        }

        // The root's attribute is the first match: the root's start tag comes first.
        return SavedQueryIdAttribute.Replace(existingFetchXml, "", 1);
    }

    /// <summary>The file's own mistakes with <c>columns</c>, refused before Dataverse is asked anything (no silent fixes).</summary>
    private static void ValidateColumns(ViewDefinition view)
    {
        if (view.Columns is not { } columns)
        {
            return;
        }

        if (view.LayoutXml is not null)
        {
            throw new InvalidDataException($"View '{view.Name}' gives both 'columns' and 'layoutXml' — use either columns or layoutXml, not both.");
        }

        if (columns.Count == 0)
        {
            throw new InvalidDataException($"View '{view.Name}' has an empty 'columns' list — list at least one column, or leave 'columns' out.");
        }

        var duplicates = columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidDataException($"View '{view.Name}' lists {string.Join(", ", duplicates.Select(d => $"'{d}'"))} more than once in 'columns' — list each column once.");
        }
    }

    /// <summary>What a view that matches nothing needs before it can be created (REQ-23).</summary>
    private static void ValidateCreate(ViewDefinition view)
    {
        // Learn: querytype "Must always be zero (0)" on create — only a
        // public view can be made; quick find, lookup, associated and the
        // rest come with the table and can only be updated.
        if (view.QueryType is not (null or "MainApplicationView"))
        {
            throw new InvalidDataException($"No view named '{view.Name}' exists on '{view.Entity}' to update, and a '{view.QueryType}' view can't be created — only a public view can (leave 'queryType' out). To update an existing one, check the name or add its 'savedQueryId'.");
        }

        if (view.Columns is null && view.LayoutXml is null)
        {
            throw new InvalidDataException($"No view named '{view.Name}' exists on '{view.Entity}', so it would be created — but a new view needs 'columns' (or a raw 'layoutXml') to know what to show.");
        }

        if (view.Columns is null && view.FetchXml is null)
        {
            throw new InvalidDataException($"No view named '{view.Name}' exists on '{view.Entity}', so it would be created — but a new view with a raw 'layoutXml' also needs a 'fetchXml' (or use 'columns', which builds both).");
        }
    }

    /// <summary>
    /// The FetchXML and LayoutXML to write. With <c>columns</c>: a generated
    /// grid, and the FetchXML (the file's, else the live view's, else a
    /// minimal one) gaining any listed column it lacks. With a raw
    /// <c>layoutXml</c>: that layout with only its <c>object</c> corrected,
    /// and the FetchXML untouched. With neither, both pass through and the
    /// table isn't looked up at all.
    /// </summary>
    private async Task<(string? FetchXml, string? LayoutXml)> BuildFetchAndLayoutAsync(Uri environmentUrl, string accessToken, ViewDefinition view, ExistingSavedQuery? existing, CancellationToken cancellationToken)
    {
        if (view.Columns is null && view.LayoutXml is null)
        {
            return (view.FetchXml, null);
        }

        var identity = await dataverseClient.TryGetEntityIdentityAsync(environmentUrl, accessToken, view.Entity, cancellationToken);

        if (view.Columns is not { } columns)
        {
            // ponytail: no live table means no type code to check against, so
            // the raw layout goes as written and Dataverse has the final word.
            return (view.FetchXml, identity is null ? view.LayoutXml : WithObjectTypeCode(view.LayoutXml!, identity.ObjectTypeCode));
        }

        if (identity is null)
        {
            throw new InvalidDataException($"Table '{view.Entity}' wasn't found, so view '{view.Name}' can't be built from 'columns' — its type code and primary id/name columns come from the live table. Create the table first (table create), or check 'entity'.");
        }

        var baseFetchXml = view.FetchXml
            ?? existing?.FetchXml
            ?? new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("mapping", "logical"), new XElement("entity", new XAttribute("name", view.Entity))).ToString(SaveOptions.DisableFormatting);

        return (
            WithAttributes(baseFetchXml, [identity.PrimaryIdAttribute, .. columns.Select(c => c.Name)]),
            BuildLayoutXml(columns, identity));
    }

    /// <summary>
    /// A grid shaped like the ones Dataverse itself exports: <c>name="resultset"</c>,
    /// <c>object</c> the table's type code (REQ-27 — Dataverse refuses a layout
    /// without it), <c>jump</c> the primary name column, <c>select</c>/<c>icon</c>/<c>preview</c>
    /// 1, and a <c>result</c> row keyed on the primary id column.
    /// </summary>
    private static string BuildLayoutXml(IReadOnlyList<ViewColumn> columns, EntityIdentity identity) =>
        new XElement("grid",
            new XAttribute("name", "resultset"),
            new XAttribute("object", identity.ObjectTypeCode),
            new XAttribute("jump", identity.PrimaryNameAttribute),
            new XAttribute("select", "1"),
            new XAttribute("icon", "1"),
            new XAttribute("preview", "1"),
            new XElement("row",
                new XAttribute("name", "result"),
                new XAttribute("id", identity.PrimaryIdAttribute),
                columns.Select(c => new XElement("cell",
                    new XAttribute("name", c.Name),
                    new XAttribute("width", c.Width ?? DefaultColumnWidth)))))
            .ToString(SaveOptions.DisableFormatting);

    /// <summary>
    /// The layout with <c>object</c> set to the live type code — returned
    /// byte-for-byte when it already is (REQ-28: exported files import
    /// unchanged), corrected when missing or wrong (an export from another
    /// environment, where custom tables' codes differ).
    /// </summary>
    private static string WithObjectTypeCode(string layoutXml, int objectTypeCode)
    {
        var grid = XElement.Parse(layoutXml);
        var code = objectTypeCode.ToString(CultureInfo.InvariantCulture);
        if ((string?)grid.Attribute("object") == code)
        {
            return layoutXml;
        }

        grid.SetAttributeValue("object", code);
        return grid.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// The FetchXML with an <c>&lt;attribute&gt;</c> added to its top-level
    /// <c>&lt;entity&gt;</c> for each of <paramref name="names"/> it doesn't
    /// already query (REQ-26: a column shown but not queried renders blank),
    /// after its existing attributes; order, filters and links are left as
    /// written. Returned byte-for-byte when nothing is missing.
    /// </summary>
    private static string WithAttributes(string fetchXml, IEnumerable<string> names)
    {
        var fetch = XElement.Parse(fetchXml);
        var entity = fetch.Element("entity")
            ?? throw new InvalidDataException("The view's FetchXML has no <entity> element to add the listed columns to.");

        var present = entity.Elements("attribute").Select(a => (string?)a.Attribute("name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = names.Where(present.Add).Select(n => new XElement("attribute", new XAttribute("name", n))).ToList();
        if (missing.Count == 0)
        {
            return fetchXml;
        }

        if (entity.Elements("attribute").LastOrDefault() is { } last)
        {
            last.AddAfterSelf(missing);
        }
        else
        {
            entity.AddFirst(missing);
        }

        return fetch.ToString(SaveOptions.DisableFormatting);
    }
}

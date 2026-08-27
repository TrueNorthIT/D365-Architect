using D365Architect.Services.Dataverse;

namespace D365Architect.Services.Conversion;

public sealed class ViewExportService(IDataverseClient dataverseClient, ViewJsonDefinitionReader reader) : IViewExportService
{
    public async Task<IReadOnlyList<ExportedView>> ExportViewsAsync(Uri environmentUrl, string accessToken, string entityLogicalName, string? solutionUniqueName, CancellationToken cancellationToken)
    {
        IReadOnlySet<Guid>? allowedSavedQueryIds = null;
        if (solutionUniqueName is not null)
        {
            allowedSavedQueryIds = await dataverseClient.TryGetSolutionSavedQueryIdsAsync(environmentUrl, accessToken, solutionUniqueName, cancellationToken)
                ?? throw new SolutionNotFoundException(solutionUniqueName);
        }

        var json = await dataverseClient.GetViewDefinitionsJsonAsync(environmentUrl, accessToken, entityLogicalName, cancellationToken);
        var views = reader.Read(json, allowedSavedQueryIds);

        // Two views can slugify to the same stem (e.g. "My View" and "My,
        // View!"); disambiguate rather than let the second silently
        // overwrite the first file on disk.
        var usedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return views
            .Select(view => new ExportedView(UniqueStem(Slugify(view.Name), usedStems), ViewYamlSerializer.ToYaml(view)))
            .ToList();
    }

    private static string UniqueStem(string stem, HashSet<string> usedStems)
    {
        var candidate = stem;
        for (var suffix = 2; !usedStems.Add(candidate); suffix++)
        {
            candidate = $"{stem}-{suffix}";
        }

        return candidate;
    }

    /// <summary>Lower-cases a view's display name into a filesystem- and URL-safe stem, e.g. "Active Accounts" -> "active-accounts".</summary>
    private static string Slugify(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');
        return slug.Length > 0 ? slug : "view";
    }
}

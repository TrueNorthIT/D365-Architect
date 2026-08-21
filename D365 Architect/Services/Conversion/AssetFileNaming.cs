namespace D365Architect.Services.Conversion;

/// <summary>
/// Shared by every export service that writes one file per asset named
/// after the asset's display name (views, forms, ...) rather than a stable
/// logical name — unlike a table or column, neither has one to lean on, so
/// the display name is slugified instead. See <see cref="Models.ViewDefinition.Name"/>/<see cref="Models.FormDefinition.Name"/>.
/// </summary>
internal static class AssetFileNaming
{
    /// <summary>Lower-cases a display name into a filesystem- and URL-safe stem, e.g. "Active Accounts" -> "active-accounts".</summary>
    public static string Slugify(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');
        return slug.Length > 0 ? slug : "unnamed";
    }

    /// <summary>Disambiguates two assets that would otherwise slugify to the same stem (e.g. "My View" and "My, View!") rather than let the second overwrite the first file on disk.</summary>
    public static string MakeUnique(string stem, HashSet<string> usedStems)
    {
        var candidate = stem;
        for (var suffix = 2; !usedStems.Add(candidate); suffix++)
        {
            candidate = $"{stem}-{suffix}";
        }

        return candidate;
    }
}

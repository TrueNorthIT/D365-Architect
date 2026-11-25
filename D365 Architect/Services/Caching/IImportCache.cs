namespace D365Architect.Services.Caching;

/// <summary>The asset kinds the import cache covers. Tables and choices are not cached yet.</summary>
public enum ImportAssetKind
{
    View,
    Form,
}

/// <summary>What <c>solution import</c> knows about one view or form file the last time it matched Dataverse.</summary>
/// <param name="Kind">View or form.</param>
/// <param name="Id">The <c>savedqueryid</c> or <c>formid</c>, as the file's YAML carries it.</param>
/// <param name="ContentHash">See <see cref="ImportCache.HashOf"/>.</param>
/// <param name="VersionNumber">The live <c>versionnumber</c> at the time the file was last known to match.</param>
public sealed record ImportCacheEntry(ImportAssetKind Kind, Guid Id, string ContentHash, long VersionNumber);

/// <summary>
/// A per-environment record of which view and form files matched Dataverse, so <c>solution import</c> can skip the full preview of
/// one that can't have changed. It may only save work: anything missing, unreadable or from another tool version reads as "no entry".
/// </summary>
public interface IImportCache
{
    /// <summary>The entry for this asset in this environment, or null when there is none or the cache can't be trusted. Never throws.</summary>
    ImportCacheEntry? Find(Uri environmentUrl, ImportAssetKind kind, Guid id);

    /// <summary>Adds or replaces these entries for this environment in one write, keeping the others. Replaces a cache file that can't be read.</summary>
    void Record(Uri environmentUrl, IReadOnlyCollection<ImportCacheEntry> entries);
}

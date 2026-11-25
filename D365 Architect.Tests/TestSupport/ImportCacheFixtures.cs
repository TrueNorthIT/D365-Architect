using System.Security.Cryptography;
using System.Text;
using D365Architect.Services.Caching;

namespace D365Architect.Tests.TestSupport;

/// <summary>
/// Hand-written stand-ins for the import cache tests. <see cref="Sha256Hex"/> is deliberately a second implementation of the file
/// hash, so a test that seeds the cache with it pins the algorithm rather than trusting <see cref="ImportCache.HashOf"/>.
/// </summary>
public static class ImportCacheFixtures
{
    /// <summary>Lower-case hex SHA-256 of the UTF-8 text with CRLF turned into LF.</summary>
    public static string Sha256Hex(string fileContent) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fileContent.Replace("\r\n", "\n"))));
}

/// <summary>
/// An in-memory <see cref="IImportCache"/> that behaves as the real one does across environments (one set of entries per
/// environment host), without touching the disk. <see cref="Entries"/> is what a test asserts on.
/// </summary>
public sealed class FakeImportCache : IImportCache
{
    private readonly Dictionary<(string Host, ImportAssetKind Kind, Guid Id), ImportCacheEntry> _entries = [];

    public IReadOnlyCollection<ImportCacheEntry> Entries => _entries.Values;

    public ImportCacheEntry? Find(Uri environmentUrl, ImportAssetKind kind, Guid id) =>
        _entries.GetValueOrDefault((environmentUrl.Host, kind, id));

    public void Record(Uri environmentUrl, IReadOnlyCollection<ImportCacheEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries[(environmentUrl.Host, entry.Kind, entry.Id)] = entry;
        }
    }

    /// <summary>Puts an entry straight in, as an earlier export or import would have left it.</summary>
    public void Seed(Uri environmentUrl, ImportAssetKind kind, Guid id, string fileContent, long versionNumber) =>
        Record(environmentUrl, [new ImportCacheEntry(kind, id, ImportCacheFixtures.Sha256Hex(fileContent), versionNumber)]);
}

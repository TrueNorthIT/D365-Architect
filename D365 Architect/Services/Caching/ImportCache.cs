using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using D365Architect.Services.Authentication;

namespace D365Architect.Services.Caching;

/// <summary>
/// <see cref="IImportCache"/> kept as one JSON file per environment, <c>&lt;directory&gt;/&lt;environment host&gt;.json</c>:
/// <code>{ "toolVersion": "1.2.3", "entries": [ { "kind": "view", "id": "&lt;guid&gt;", "contentHash": "&lt;hex&gt;", "versionNumber": 123 } ] }</code>
/// A file written by a different <c>toolVersion</c> is ignored and replaced on the next <see cref="Record"/>.
/// Neither method throws: the cache only saves work, so a file that can't be read or written just means a full preview next time.
/// </summary>
public sealed class ImportCache(string directory, string toolVersion) : IImportCache
{
    /// <summary>Under the folder the sign-in cache uses: <c>%LOCALAPPDATA%\d365architect\import-cache</c>.</summary>
    public static string DefaultDirectory() => Path.Combine(MsalAuthenticationService.GetAppDataDirectory(), "import-cache");

    /// <summary>Lower-case hex SHA-256 of the file's UTF-8 text with CRLF line endings turned into LF, so a CRLF and an LF checkout match.</summary>
    public static string HashOf(string fileContent)
    {
        var bytes = Encoding.UTF8.GetBytes(fileContent.Replace("\r\n", "\n"));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public ImportCacheEntry? Find(Uri environmentUrl, ImportAssetKind kind, Guid id) =>
        TryLoad(environmentUrl)?.GetValueOrDefault((kind, id));

    public void Record(Uri environmentUrl, IReadOnlyCollection<ImportCacheEntry> entries)
    {
        var merged = TryLoad(environmentUrl) ?? [];
        foreach (var entry in entries)
        {
            merged[(entry.Kind, entry.Id)] = entry;
        }

        var file = new Dictionary<string, object>
        {
            ["toolVersion"] = toolVersion,
            ["entries"] = merged.Values.Select(e => new Dictionary<string, object>
            {
                ["kind"] = e.Kind == ImportAssetKind.View ? "view" : "form",
                ["id"] = e.Id,
                ["contentHash"] = e.ContentHash,
                ["versionNumber"] = e.VersionNumber,
            }).ToList(),
        };

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(PathFor(environmentUrl), JsonSerializer.Serialize(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the write only costs a full preview next run.
        }
    }

    private string PathFor(Uri environmentUrl) => Path.Combine(directory, $"{environmentUrl.Host}.json");

    /// <summary>The environment's entries, or null when there is no file or it can't be trusted (unreadable, wrong shape, another tool version).</summary>
    private Dictionary<(ImportAssetKind Kind, Guid Id), ImportCacheEntry>? TryLoad(Uri environmentUrl)
    {
        try
        {
            var path = PathFor(environmentUrl);
            if (!File.Exists(path))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.GetProperty("toolVersion").GetString() != toolVersion)
            {
                return null;
            }

            var entries = new Dictionary<(ImportAssetKind Kind, Guid Id), ImportCacheEntry>();
            foreach (var element in root.GetProperty("entries").EnumerateArray())
            {
                var kind = element.GetProperty("kind").GetString() switch
                {
                    "view" => ImportAssetKind.View,
                    "form" => ImportAssetKind.Form,
                    _ => throw new FormatException("Unknown asset kind."),
                };
                var id = element.GetProperty("id").GetGuid();
                var contentHash = element.GetProperty("contentHash").GetString() ?? throw new FormatException("No content hash.");
                var versionNumber = element.GetProperty("versionNumber").GetInt64();
                entries[(kind, id)] = new ImportCacheEntry(kind, id, contentHash, versionNumber);
            }

            return entries;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

using D365Architect.Services.Authentication;
using D365Architect.Services.Caching;
using D365Architect.Services.Conversion;
using D365Architect.Services.Dataverse;
using Spectre.Console;

namespace D365Architect.Commands.Solution;

/// <summary>
/// The <see cref="IImportCache"/> side of <c>solution import</c> for views and forms. <see cref="PreviewAsync"/> counts a file as
/// unchanged without the full preview when its hash matches the entry the last export or import recorded and the live
/// <c>versionnumber</c> still matches too; anything short of that evidence gets the full preview. The live versions are read once per
/// table and kind per run. <see cref="RecordAsync"/>, at the end of the run, records what the run proved: a full preview that found the
/// file unchanged (with the version read before it), and an applied change (with the version re-read after the write, so the tool's own
/// write isn't seen as drift next time). Nothing is recorded for a change that wasn't written.
/// </summary>
internal sealed class ImportCacheCheck(IDataverseClient dataverseClient, IImportCache importCache, Func<CancellationToken, Task<AuthenticatedContext>> getContextAsync, bool skip)
{
    private sealed record CachedAsset(ImportAssetKind Kind, Guid Id, string Table, string ContentHash, long? LiveVersion);

    private readonly Dictionary<(ImportAssetKind Kind, string Table), IReadOnlyDictionary<Guid, long>> _liveVersions = [];
    private readonly List<ImportCacheEntry> _matched = [];
    private readonly List<CachedAsset> _applied = [];
    private Uri? _environmentUrl;

    /// <param name="kind">Whether the file is a view or a form.</param>
    /// <param name="assetPath">The <c>*.view.yml</c> or <c>*.form.yml</c> file.</param>
    /// <param name="printUnchanged">Whether to print a "No changes" line for a file that is skipped.</param>
    /// <param name="fullPreviewAsync">The file's own full preview, for when it can't be skipped.</param>
    /// <param name="cancellationToken"></param>
    public async Task<PreparedImport> PreviewAsync(ImportAssetKind kind, string assetPath, bool printUnchanged, Func<CancellationToken, Task<PreparedImport>> fullPreviewAsync, CancellationToken cancellationToken)
    {
        var asset = await TryReadAsync(kind, assetPath, cancellationToken);
        if (asset is null)
        {
            return await fullPreviewAsync(cancellationToken);
        }

        var entry = importCache.Find(_environmentUrl!, kind, asset.Id);
        if (skip && entry is not null && entry.ContentHash == asset.ContentHash && entry.VersionNumber == asset.LiveVersion)
        {
            if (printUnchanged)
            {
                AnsiConsole.MarkupLine($"[green]No changes[/] — '{assetPath.EscapeMarkup()}' hasn't been edited since the last export or import, and hasn't changed in Dataverse since (skipped).");
            }

            return PreparedImport.Unchanged with { PrintedOutput = printUnchanged };
        }

        var prepared = await fullPreviewAsync(cancellationToken);
        if (prepared.Outcome == ImportOutcome.Unchanged && asset.LiveVersion is { } liveVersion)
        {
            _matched.Add(new ImportCacheEntry(kind, asset.Id, asset.ContentHash, liveVersion));
        }

        if (prepared.Outcome == ImportOutcome.Changed)
        {
            var applyAsync = prepared.ApplyAsync!;
            return prepared with
            {
                ApplyAsync = async ct =>
                {
                    var written = await applyAsync(ct);
                    if (written)
                    {
                        _applied.Add(asset);
                    }

                    return written;
                },
            };
        }

        return prepared;
    }

    /// <summary>Records, in one cache write, every file this run found matching or wrote. Call once the run's changes are applied.</summary>
    public async Task RecordAsync(CancellationToken cancellationToken)
    {
        if (_environmentUrl is null)
        {
            return;
        }

        var entries = new List<ImportCacheEntry>(_matched);
        foreach (var table in _applied.GroupBy(a => (a.Kind, a.Table)))
        {
            IReadOnlyDictionary<Guid, long> versionsAfterWrite;
            try
            {
                versionsAfterWrite = await GetVersionsAsync(table.Key.Kind, table.Key.Table, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or AuthenticationRequiredException)
            {
                // Without the version after the write there's nothing to record; the next run previews these in full.
                continue;
            }

            foreach (var asset in table)
            {
                if (versionsAfterWrite.TryGetValue(asset.Id, out var version))
                {
                    entries.Add(new ImportCacheEntry(asset.Kind, asset.Id, asset.ContentHash, version));
                }
            }
        }

        if (entries.Count > 0)
        {
            importCache.Record(_environmentUrl, entries);
        }
    }

    /// <summary>The file's id, table, hash and live version; null when it has no id or can't be read, so it gets the full preview (which reports why).</summary>
    private async Task<CachedAsset?> TryReadAsync(ImportAssetKind kind, string assetPath, CancellationToken cancellationToken)
    {
        string text;
        Guid? id;
        string table;
        try
        {
            text = await File.ReadAllTextAsync(assetPath, cancellationToken);
            if (kind == ImportAssetKind.View)
            {
                var view = ViewYamlDeserializer.FromYaml(text);
                (id, table) = (view?.SavedQueryId, view?.Entity ?? "");
            }
            else
            {
                var form = FormYamlDeserializer.FromYaml(text);
                (id, table) = (form?.FormId, form?.Entity ?? "");
            }

            _environmentUrl ??= (await getContextAsync(cancellationToken)).EnvironmentUrl;
        }
        catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException or AuthenticationRequiredException)
        {
            return null;
        }

        if (id is null || table.Length == 0)
        {
            return null;
        }

        var key = (kind, table.ToLowerInvariant());
        if (!_liveVersions.TryGetValue(key, out var versions))
        {
            try
            {
                versions = await GetVersionsAsync(kind, table, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or AuthenticationRequiredException)
            {
                // No versions means no skips for this table; each file's full preview reports the problem itself.
                versions = new Dictionary<Guid, long>();
            }

            _liveVersions[key] = versions;
        }

        long? liveVersion = versions.TryGetValue(id.Value, out var found) ? found : null;
        return new CachedAsset(kind, id.Value, table, ImportCache.HashOf(text), liveVersion);
    }

    // A fresh context each time, as each file's own preview gets one, so a long run doesn't outlive its token.
    private async Task<IReadOnlyDictionary<Guid, long>> GetVersionsAsync(ImportAssetKind kind, string table, CancellationToken cancellationToken)
    {
        var auth = await getContextAsync(cancellationToken);
        return kind == ImportAssetKind.View
            ? await dataverseClient.GetSavedQueryVersionsAsync(auth.EnvironmentUrl, auth.AccessToken, table, cancellationToken)
            : await dataverseClient.GetSystemFormVersionsAsync(auth.EnvironmentUrl, auth.AccessToken, table, cancellationToken);
    }
}

using D365Architect.Services.Caching;
using D365Architect.Tests.TestSupport;
using Xunit;

namespace D365Architect.Tests.Services.Caching;

/// <summary>
/// Covers <see cref="ImportCache"/>, the per-environment record of which view and form files matched Dataverse that lets
/// <c>solution import</c> skip the full preview. The one rule it has to keep is that it may only save work: a file it can't read,
/// one from another tool version, or an environment it has never seen all read as "no entry", never as "unchanged".
/// Each test uses its own temporary folder as the cache folder. Tagged with the "Feature" trait "skip-unchanged-assets".
/// </summary>
[Trait("Feature", "skip-unchanged-assets")]
public sealed class ImportCacheTests : IDisposable
{
    private const string ToolVersion = "1.2.3";
    private static readonly Uri Environment = new("https://test.crm.dynamics.com");
    private static readonly Uri OtherEnvironment = new("https://other.crm.dynamics.com");
    private static readonly Guid ViewId = new("db985fe6-a889-f111-8077-002248426c39");
    private static readonly Guid FormId = new("2b0c3c0e-6f1a-4c0e-9a51-6d3f0c7a1e22");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "d365architect-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ImportCache CreateCache(string toolVersion = ToolVersion) => new(_directory, toolVersion);

    private string FileFor(Uri environment) => Path.Combine(_directory, $"{environment.Host}.json");

    private static ImportCacheEntry ViewEntry(long version = 100, string content = "name: All projects") =>
        new(ImportAssetKind.View, ViewId, ImportCacheFixtures.Sha256Hex(content), version);

    // The cache lives beside the sign-in cache, not in the repo.
    // Code path: ImportCache.DefaultDirectory.
    [Fact]
    public void DefaultDirectory_IsImportCacheUnderTheFolderTheSignInCacheUses()
    {
        var expected = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "d365architect", "import-cache");

        Expect.Equal(expected, ImportCache.DefaultDirectory(), "The import cache should be kept in %LOCALAPPDATA%\\d365architect\\import-cache, next to the sign-in cache.");
    }

    // Design: "the file's content hash (SHA-256 ...)".
    // Code path: ImportCache.HashOf.
    [Fact]
    public void HashOf_Text_IsTheLowerCaseHexSha256()
    {
        var hash = ImportCache.HashOf("abc");

        Expect.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash, "The hash should be the lower-case hex SHA-256 of the text.");
    }

    // REQ-7: an unedited file counts as unchanged whichever way git checked it out.
    // Code path: ImportCache.HashOf normalises CRLF to LF.
    [Fact]
    [Trait("Requirement", "REQ-7")]
    public void HashOf_SameTextWithCrlfAndLfLineEndings_GivesTheSameHash()
    {
        var lf = ImportCache.HashOf("name: All projects\nentity: tn_project\n");
        var crlf = ImportCache.HashOf("name: All projects\r\nentity: tn_project\r\n");

        Expect.Equal(lf, crlf, "A CRLF checkout of a file should hash the same as an LF checkout, or an unedited file would look edited.");
    }

    // REQ-8: an edited file must not match what was recorded.
    [Fact]
    [Trait("Requirement", "REQ-8")]
    public void HashOf_EditedText_GivesADifferentHash()
    {
        var before = ImportCache.HashOf("name: All projects\n");
        var after = ImportCache.HashOf("name: All projects (renamed)\n");

        Expect.True(before != after, "An edited file should hash differently from the version that was recorded.");
    }

    // REQ-10: nothing recorded is "no entry".
    // Code path: ImportCache.Find with no file on disk.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public void Find_NothingRecordedYet_ReturnsNull()
    {
        var entry = CreateCache().Find(Environment, ImportAssetKind.View, ViewId);

        Expect.True(entry is null, "Without a cache file there is no evidence, so Find should return null.");
    }

    // REQ-11: what import records is what the next run reads back.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public void Record_ThenFind_ReturnsTheEntry()
    {
        var cache = CreateCache();
        var recorded = ViewEntry(version: 4321);

        cache.Record(Environment, [recorded]);
        var found = cache.Find(Environment, ImportAssetKind.View, ViewId);

        Expect.Equal(recorded, found, "Find should return the entry that was recorded, with its hash and version number.");
    }

    // The kind is part of the key: a view and a form can never share an entry.
    [Fact]
    public void Record_ViewAndFormWithTheSameId_AreKeptApart()
    {
        var cache = CreateCache();
        var view = new ImportCacheEntry(ImportAssetKind.View, ViewId, ImportCacheFixtures.Sha256Hex("view"), 1);
        var form = new ImportCacheEntry(ImportAssetKind.Form, ViewId, ImportCacheFixtures.Sha256Hex("form"), 2);

        cache.Record(Environment, [view, form]);

        Expect.Equal(view, cache.Find(Environment, ImportAssetKind.View, ViewId), "The view entry should not be overwritten by a form with the same id.");
        Expect.Equal(form, cache.Find(Environment, ImportAssetKind.Form, ViewId), "The form entry should not be overwritten by a view with the same id.");
    }

    // REQ-11: recording one run's files must not forget the files an earlier run recorded.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public void Record_Twice_KeepsEarlierEntriesAndReplacesTheSameAsset()
    {
        var cache = CreateCache();
        var form = new ImportCacheEntry(ImportAssetKind.Form, FormId, ImportCacheFixtures.Sha256Hex("form"), 7);
        cache.Record(Environment, [ViewEntry(version: 100), form]);

        var newerView = ViewEntry(version: 101, content: "name: All projects (renamed)");
        cache.Record(Environment, [newerView]);

        Expect.Equal(newerView, cache.Find(Environment, ImportAssetKind.View, ViewId), "Recording a view again should replace its earlier entry.");
        Expect.Equal(form, cache.Find(Environment, ImportAssetKind.Form, FormId), "Recording a view should keep the form recorded earlier.");
    }

    // Design: "one JSON file per environment ... <environment host>.json".
    [Fact]
    public void Record_WritesOneFileNamedAfterTheEnvironmentHost()
    {
        CreateCache().Record(Environment, [ViewEntry()]);

        Expect.Sequence(["test.crm.dynamics.com.json"], Directory.GetFiles(_directory).Select(Path.GetFileName).OfType<string>().ToList(), "The cache should be one JSON file named after the environment's host.");
    }

    // REQ-9: entries recorded against one environment say nothing about another.
    // Code path: ImportCache.Find reads the file for the environment it is asked about.
    [Fact]
    [Trait("Requirement", "REQ-9")]
    public void Find_EnvironmentOtherThanTheRecordedOne_ReturnsNull()
    {
        var cache = CreateCache();
        cache.Record(Environment, [ViewEntry()]);

        var entry = cache.Find(OtherEnvironment, ImportAssetKind.View, ViewId);

        Expect.True(entry is null, "An entry recorded for test.crm.dynamics.com should not be offered for other.crm.dynamics.com.");
    }

    // REQ-10: normalisation or export logic may differ between tool versions, so a cache from another version is not evidence.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public void Find_CacheWrittenByAnotherToolVersion_ReturnsNull()
    {
        CreateCache("1.2.3").Record(Environment, [ViewEntry()]);

        var entry = CreateCache("1.3.0").Find(Environment, ImportAssetKind.View, ViewId);

        Expect.True(entry is null, "A cache written by tool version 1.2.3 should be ignored by version 1.3.0.");
    }

    // REQ-10 with REQ-11: after an upgrade the old entries are dropped, not carried across under the new version.
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public void Record_AfterAToolVersionChange_DropsTheEntriesOfTheOldVersion()
    {
        CreateCache("1.2.3").Record(Environment, [ViewEntry()]);
        var form = new ImportCacheEntry(ImportAssetKind.Form, FormId, ImportCacheFixtures.Sha256Hex("form"), 7);

        var upgraded = CreateCache("1.3.0");
        upgraded.Record(Environment, [form]);

        Expect.Equal(form, upgraded.Find(Environment, ImportAssetKind.Form, FormId), "The entry recorded by the new version should be found.");
        Expect.True(upgraded.Find(Environment, ImportAssetKind.View, ViewId) is null, "The view recorded by the old tool version should not be carried across to the new one.");
    }

    // REQ-10: an unreadable cache file means a full preview, not a crash and not "unchanged".
    [Fact]
    [Trait("Requirement", "REQ-10")]
    public void Find_CorruptFile_ReturnsNullWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FileFor(Environment), "{ this is not json");

        var entry = CreateCache().Find(Environment, ImportAssetKind.View, ViewId);

        Expect.True(entry is null, "A corrupt cache file should read as no entry.");
    }

    // REQ-10: valid JSON of the wrong shape, or an entry missing a field, is also doubt.
    [Theory]
    [Trait("Requirement", "REQ-10")]
    [InlineData("[]")]
    [InlineData("""{ "toolVersion": "1.2.3" }""")]
    [InlineData("""{ "toolVersion": "1.2.3", "entries": [ { "kind": "view", "id": "db985fe6-a889-f111-8077-002248426c39" } ] }""")]
    [InlineData("""{ "toolVersion": "1.2.3", "entries": [ { "kind": "view", "id": "not-a-guid", "contentHash": "abc", "versionNumber": 1 } ] }""")]
    public void Find_FileOfTheWrongShape_ReturnsNullWithoutThrowing(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FileFor(Environment), json);

        var entry = CreateCache().Find(Environment, ImportAssetKind.View, ViewId);

        Expect.True(entry is null, $"A cache file of the wrong shape should read as no entry. File: <{json}>");
    }

    // REQ-11: a cache that went bad is replaced by the next import's record, so it heals.
    [Fact]
    [Trait("Requirement", "REQ-11")]
    public void Record_OverACorruptFile_ReplacesItSoTheNextFindWorks()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FileFor(Environment), "{ this is not json");
        var cache = CreateCache();
        var recorded = ViewEntry();

        cache.Record(Environment, [recorded]);

        Expect.Equal(recorded, cache.Find(Environment, ImportAssetKind.View, ViewId), "Recording over a corrupt cache file should replace it with a readable one.");
    }

    // Pins the file format the cache documents, so a cache written by an earlier build of the same version stays readable.
    // Code path: ImportCache.Find parses { toolVersion, entries: [ { kind, id, contentHash, versionNumber } ] }.
    [Fact]
    public void Find_ReadsTheDocumentedFileFormat()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FileFor(Environment), $$"""
            { "toolVersion": "{{ToolVersion}}", "entries": [
                { "kind": "view", "id": "{{ViewId}}", "contentHash": "abc123", "versionNumber": 12345678901 },
                { "kind": "form", "id": "{{FormId}}", "contentHash": "def456", "versionNumber": 7 } ] }
            """);
        var cache = CreateCache();

        Expect.Equal(new ImportCacheEntry(ImportAssetKind.View, ViewId, "abc123", 12345678901), cache.Find(Environment, ImportAssetKind.View, ViewId), "The view entry should be read from the documented format, with a version number above int range.");
        Expect.Equal(new ImportCacheEntry(ImportAssetKind.Form, FormId, "def456", 7), cache.Find(Environment, ImportAssetKind.Form, FormId), "The form entry should be read from the documented format.");
    }
}

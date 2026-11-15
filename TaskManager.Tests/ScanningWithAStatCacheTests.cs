using System.Text;
using BoardSync;
using BoardSync.PublicDTOs;
using BoardSync.Sync;

namespace TaskManager.Tests;

/// <summary>
/// One scan per pass: a stat cache (size, last write, hash per path, git's racy rule) shared by the scan, the git guard, the marker list and the status line, so an unchanged file is not read;
/// the settle window and the ignore list that keep half-written and temp files out of the storage; and one pooled connection handler for every S3 storage.
/// Clocks and last-write times are set by hand; nothing waits.
/// </summary>
public sealed class ScanningWithAStatCacheTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-scan-");
    private readonly DateTime _t0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private DateTime _clock = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private string Root => Path.Combine(_temp.FullName, "changes");
    private string State => Path.Combine(_temp.FullName, "state");
    private string Storage => Path.Combine(_temp.FullName, "storage");
    private static readonly TimeSpan TenSeconds = TimeSpan.FromSeconds(10);

    private string Write(string relative, string text, DateTime written)
    {
        string file = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, written);
        return file;
    }

    /// <summary>One scan the way a pass makes it: the cache is opened from the state folder, used, and saved. Returns the files and how many buffers were read for hashing.</summary>
    private (Dictionary<string, LocalFile> Files, int Hashed) ScanOnce(DateTime now, TimeSpan settle = default)
    {
        StatCache cache = StatCache.Open(new SyncStateStore(State), now);
        int hashed = 0;
        Dictionary<string, LocalFile> files = LocalTree.Scan(Root, [], [], _ => hashed++, cache, settle);
        cache.Save();
        return (files, hashed);
    }

    private Task<SyncReport> Sync(bool realClock = false) =>
        BoardSyncApi.Sync(new SyncOptions(Root, State, BoardSyncApi.FolderStorage(Storage), "A", realClock ? null : () => _clock, SettleWindow: realClock ? default : TenSeconds));

    // ---- the stat cache -------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "A second scan hashes only files whose size or last write changed")]
    public void ASecondScanHashes()
    {
        DateTime old = _t0.AddHours(-1);
        Write("one/a.md", "# a\n", old);
        Write("one/b.md", "# b\n", old);
        Write("two/c.md", "# c\n", old);

        (Dictionary<string, LocalFile> first, int hashedFirst) = ScanOnce(_t0);
        Assert.Equal(3, hashedFirst);

        (Dictionary<string, LocalFile> second, int hashedSecond) = ScanOnce(_t0);
        Assert.Equal(0, hashedSecond);                                              // nothing read: size and time are what the cache saw
        Assert.Equal(first.ToDictionary(p => p.Key, p => p.Value.Hash), second.ToDictionary(p => p.Key, p => p.Value.Hash));
        Assert.Equal("# b\n", Encoding.UTF8.GetString(second["one/b.md"].Bytes));    // no bytes were kept: they are read again when something needs them

        Write("one/b.md", "# b changed\n", old);                                    // a new length with the old time
        (Dictionary<string, LocalFile> third, int hashedThird) = ScanOnce(_t0);
        Assert.Equal(1, hashedThird);
        Assert.NotEqual(first["one/b.md"].Hash, third["one/b.md"].Hash);

        File.SetLastWriteTimeUtc(Path.Combine(Root, "two/c.md"), old.AddMinutes(5));     // the same content with a new time
        Assert.Equal(1, ScanOnce(_t0).Hashed);
        Assert.Equal(0, ScanOnce(_t0).Hashed);
    }

    [Fact(DisplayName = "A file written in the same second as the cache is hashed again (racy rule)")]
    public void AFileWrittenIn()
    {
        Write("fresh.md", "# fresh\n", _t0.AddSeconds(-1));        // inside git's racy window: the cache cannot tell a later same-size write from this one
        Write("old.md", "# old\n", _t0.AddHours(-1));

        Assert.Equal(2, ScanOnce(_t0).Hashed);
        Assert.Equal(1, ScanOnce(_t0).Hashed);                      // only the racy one
        Assert.Equal(1, ScanOnce(_t0).Hashed);

        Assert.Equal(1, ScanOnce(_t0.AddMinutes(1)).Hashed);       // the scan a minute later still reads it once (the cache it opened was racy for it) and saves a cache it is not racy for
        Assert.Equal(0, ScanOnce(_t0.AddMinutes(2)).Hashed);
    }

    [Fact(DisplayName = "A file edited to the same length with a new time is hashed and its new hash is seen")]
    public void AFileEditedTo()
    {
        DateTime old = _t0.AddHours(-1);
        Write("a.md", "# aaaa\n", old);
        Dictionary<string, LocalFile> first = ScanOnce(_t0).Files;

        Write("a.md", "# bbbb\n", old.AddMinutes(1));
        (Dictionary<string, LocalFile> second, int hashed) = ScanOnce(_t0);

        Assert.Equal(1, hashed);
        Assert.NotEqual(first["a.md"].Hash, second["a.md"].Hash);
        Assert.Equal(SyncModels.HashOf(Encoding.UTF8.GetBytes("# bbbb\n")), second["a.md"].Hash);
    }

    [Fact(DisplayName = "A missing or damaged cache means everything is hashed")]
    public void AMissingOrDamaged()
    {
        DateTime old = _t0.AddHours(-1);
        Write("a.md", "# a\n", old);
        Write("b.md", "# b\n", old);

        Assert.Equal(2, ScanOnce(_t0).Hashed);                      // no cache yet
        File.WriteAllText(Path.Combine(State, "scan.json"), "{ not json");
        Assert.Equal(2, ScanOnce(_t0).Hashed);                      // damaged
        Assert.Equal(0, ScanOnce(_t0).Hashed);                      // and written again
    }

    [Fact(DisplayName = "The git guard and the marker repair read only files whose hash differs from the last synced one")]
    public async Task TheGitGuardAnd()
    {
        DateTime old = DateTime.UtcNow.AddHours(-1);                 // the guard has no injected clock: the real one decides what is racy
        Write("a.md", "# a\n", old);
        Write("b.md", "# b\n", old);
        SyncReport first = await Sync(realClock: true);
        Assert.Equal(2, first.Uploaded.Count);
        SyncStateFile synced = new SyncStateStore(State).Load()!;
        Assert.True(File.Exists(Path.Combine(State, "scan.json")));  // the pass left its scan behind

        List<string> reads = [];
        Assert.Empty(GitGuard.ConflictedFiles(Root, synced.Entries, StatCache.Open(new SyncStateStore(State), DateTime.UtcNow), reads.Add));
        Assert.Empty(reads);                                         // both files are what the last sync wrote and the cache says so: no file was opened

        Write("a.md", "# a\nand a new line\n", old);               // changed since the last sync
        string marked = "<<<<<<< ours\nx\n=======\ny\n>>>>>>> theirs\n";
        Write("c.md", marked, old);                                  // new, with markers
        reads.Clear();
        List<string> found = GitGuard.ConflictedFiles(Root, synced.Entries, StatCache.Open(new SyncStateStore(State), DateTime.UtcNow), reads.Add);
        Assert.Equal(["c.md"], found);
        Assert.Equal(["a.md", "c.md"], reads.Order(StringComparer.Ordinal));    // b.md, unchanged, was not read
    }

    [Fact(DisplayName = "The marker list and the status line use the cache the pass made")]
    public async Task TheMarkerListAnd()
    {
        DateTime old = DateTime.UtcNow.AddHours(-1);
        string marked = "<<<<<<< ours\nx\n=======\ny\n>>>>>>> theirs\n";
        string plain = new string('z', marked.Length - 1) + "\n";    // the same length as the marked text
        Write("b.md", plain, old);
        await Sync(realClock: true);
        string git = Path.Combine(_temp.FullName, "git");
        Directory.CreateDirectory(git);

        Assert.Empty(BoardSyncApi.MarkedFiles(Root, State));
        Assert.Null(BoardSyncApi.GitPause(Root, git, State));

        // Same length and same time as the cache saw: as in git, such a file is not read again (a real edit changes one of them).
        Write("b.md", marked, old);
        Assert.Empty(BoardSyncApi.MarkedFiles(Root, State));
        Assert.Null(BoardSyncApi.GitPause(Root, git, State));

        Write("b.md", marked + "\n", old);
        Assert.Equal(["b.md"], BoardSyncApi.MarkedFiles(Root, State));
        Assert.Contains("merge conflict markers", BoardSyncApi.GitPause(Root, git, State));
    }

    // ---- the settle window and the ignore list --------------------------------------------------------------------------------------

    [Fact(DisplayName = "A file written less than ten seconds before the scan waits for the next pass and is reported as settling")]
    public async Task AFileWrittenLess()
    {
        Write("stable.md", "# stable\n", _clock.AddHours(-1));
        Write("fresh.md", "# fresh\n", _clock.AddSeconds(-3));

        SyncReport first = await Sync();
        Assert.Equal(["stable.md"], first.Uploaded);
        Assert.Equal(["fresh.md"], first.Settling);
        Assert.False(File.Exists(Path.Combine(Storage, "objects")) && Directory.EnumerateFiles(Storage, "*fresh*", SearchOption.AllDirectories).Any());

        _clock = _clock.AddSeconds(11);
        SyncReport second = await Sync();
        Assert.Equal(["fresh.md"], second.Uploaded);
        Assert.Empty(second.Settling);
        Assert.Equal(SyncOutcome.Synced, second.Outcome);

        SyncReport third = await Sync();
        Assert.Equal(SyncOutcome.NothingToDo, third.Outcome);
        Assert.Empty(third.Settling);
    }

    [Fact(DisplayName = "A file that keeps changing for five minutes is taken anyway")]
    public async Task AFileThatKeeps()
    {
        Write("stable.md", "# stable\n", _clock.AddHours(-1));
        Write("busy.md", "# busy 0\n", _clock.AddSeconds(-1));
        Assert.Equal(["busy.md"], (await Sync()).Settling);

        _clock = _t0.AddMinutes(2);
        Write("busy.md", "# busy 1\n", _clock.AddSeconds(-1));       // still being written
        Assert.Equal(["busy.md"], (await Sync()).Settling);

        _clock = _t0.AddMinutes(4);
        Write("busy.md", "# busy 2\n", _clock.AddSeconds(-1));
        SyncReport almost = await Sync();
        Assert.Equal(["busy.md"], almost.Settling);
        Assert.DoesNotContain("busy.md", almost.Uploaded);

        _clock = _t0.AddMinutes(5);
        Write("busy.md", "# busy 3\n", _clock.AddSeconds(-1));
        SyncReport taken = await Sync();
        Assert.Equal(["busy.md"], taken.Uploaded);
        Assert.Empty(taken.Settling);
    }

    [Fact(DisplayName = "Temp and editor files are never scanned")]
    public async Task TempAndEditorFiles()
    {
        DateTime old = _clock.AddHours(-1);
        string[] ignored = ["notes.tmp", "~$report.docx", "page.md.swp", ".#page.md", "page.md~", ".rtm-lock", "sub/x.tmp", "sub/.rtm-thing.md", "item.md.guid.rtm-sync-tmp"];
        foreach (string name in ignored) Write(name, "scratch\n", old);
        Write("real.md", "# real\n", old);
        Write("sub/also-real.md", "# real\n", old);

        Assert.Equal(["real.md", "sub/also-real.md"], ScanOnce(_clock).Files.Keys.Order(StringComparer.Ordinal));

        SyncReport report = await Sync();
        Assert.Equal(["real.md", "sub/also-real.md"], report.Uploaded.Order(StringComparer.Ordinal));
        foreach (string name in ignored) Assert.True(File.Exists(Path.Combine(Root, name)), name);       // left alone, not deleted
    }

    // ---- one pooled connection ------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Two passes share one HTTP connection handler")]
    public void TwoPassesShareOne()
    {
        using BoardSync.Storage.S3Storage first = (BoardSync.Storage.S3Storage)BoardSyncApi.S3Storage("https://a.r2.example.test", "bucket", "one", "key", "secret");
        using BoardSync.Storage.S3Storage second = (BoardSync.Storage.S3Storage)BoardSyncApi.S3Storage("https://b.r2.example.test", "bucket", "two", "key", "secret");

        Assert.Same(first.Handler, second.Handler);
        SocketsHttpHandler handler = Assert.IsType<SocketsHttpHandler>(first.Handler);
        Assert.Equal(TimeSpan.FromMinutes(5), handler.PooledConnectionLifetime);            // a pooled connection is renewed, so a changed address is picked up
        Assert.False(handler.AllowAutoRedirect);                                            // a signed request is for one address
        Assert.False(handler.UseCookies);

        first.Dispose();                                                                    // disposing a storage never takes the shared handler away
        using BoardSync.Storage.S3Storage third = (BoardSync.Storage.S3Storage)BoardSyncApi.S3Storage("https://a.r2.example.test", "bucket", "one", "key", "secret");
        Assert.Same(handler, third.Handler);
    }
}

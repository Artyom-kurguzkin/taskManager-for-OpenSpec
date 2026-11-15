using System.Text;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;
using BoardSync.Storage;
using BoardSync.Sync;

namespace TaskManager.Tests;

/// <summary>
/// How a pass moves objects: six at a time, downloads streamed into files in the state folder instead of held in memory, a copy the state folder already has not fetched again,
/// and a manifest that is compact with a short history. Parallelism is proved with gates (a request waits until six are inside at once), never with sleeps.
/// </summary>
public sealed class TransferringObjectsTests : IDisposable
{
    private const string Tasks = "add-health-check/tasks.md";
    private const string SeedTasks = "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 write the test\n- [ ] 1.2 write the code\n";

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-transfer-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private sealed record Computer(string Id, string Root, string State, IBoardStorage Storage);

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");

    private Computer NewComputer(string id, IBoardStorage? storage = null) =>
        new(id, Path.Combine(_temp.FullName, id, "changes"), Path.Combine(_temp.FullName, id, "state"), storage ?? BoardSyncApi.FolderStorage(StorageFolder));

    private Task<SyncReport> Sync(Computer c) => BoardSyncApi.Sync(new SyncOptions(c.Root, c.State, c.Storage, c.Id, () => _now));

    private void Write(Computer c, string path, string text)
    {
        string file = Path.Combine(c.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, _now);
    }

    private static string Read(Computer c, string path) => File.ReadAllText(Path.Combine(c.Root, path));

    private static string[] Eight => [.. Enumerable.Range(1, 8).Select(i => $"w/file{i}.md")];

    private void WriteEight(Computer c) { foreach (string path in Eight) Write(c, path, $"# {path}\n\ncontent of {path}\n"); }

    // ---- a storage that can be gated, counted and watched ----------------------------------------------------------------------------

    /// <summary>One side of a gate: requests wait inside until <c>release</c> of them are inside at once, then all go on. A sequential caller never gets there (the tests carry a timeout).</summary>
    private sealed class Gate(int release)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inside;
        public int Max { get; private set; }

        public async Task Pass()
        {
            int now;
            lock (_open) { now = ++_inside; Max = Math.Max(Max, now); }
            if (now >= release) _open.TrySetResult();
            await _open.Task;
            lock (_open) _inside--;
        }
    }

    private sealed class WatchedStorage(IBoardStorage inner, string? stateFolder = null) : IBoardStorage
    {
        public Gate? ObjectGets { get; set; }
        public Gate? ObjectPuts { get; set; }
        public List<string> Puts { get; } = [];
        public int ObjectGetCalls { get; private set; }
        public int ObjectGetToCalls { get; private set; }
        /// <summary>For every streamed object: was a non-empty file already in the state folder's fetch folder when the storage had written it?</summary>
        public List<bool> StreamedToAFile { get; } = [];

        public bool ConditionalWrites => inner.ConditionalWrites;

        public async Task<StoredFile?> Get(string path, CancellationToken ct = default)
        {
            if (path.StartsWith("objects/", StringComparison.Ordinal))
            {
                lock (Puts) ObjectGetCalls++;
                if (ObjectGets is { } gate) await gate.Pass();
            }
            return await inner.Get(path, ct);
        }

        public async Task<bool> GetTo(string path, Stream destination, CancellationToken ct = default)
        {
            lock (Puts) ObjectGetToCalls++;
            if (ObjectGets is { } gate) await gate.Pass();
            bool found = await inner.GetTo(path, destination, ct);
            if (found && stateFolder is not null)
            {
                await destination.FlushAsync(ct);
                string fetch = Path.Combine(stateFolder, "fetch");
                bool onDisk = Directory.Exists(fetch) && Directory.EnumerateFiles(fetch).Any(f => new FileInfo(f).Length > 0);
                lock (Puts) StreamedToAFile.Add(onDisk);
            }
            return found;
        }

        public Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default) => inner.GetIfChanged(path, version, ct);

        public async Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default)
        {
            if (path.StartsWith("objects/", StringComparison.Ordinal) && ObjectPuts is { } gate) await gate.Pass();
            PutResult result = await inner.Put(path, bytes, condition, ct);
            lock (Puts) Puts.Add(path);
            return result;
        }

        public Task Delete(string path, CancellationToken ct = default) => inner.Delete(path, ct);
        public Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default) => inner.List(prefix, ct);
    }

    // ---- the facts -----------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Six objects are fetched at once, not one after another", Timeout = 30000)]
    public async Task SixObjectsAreFetchedAtOnce()
    {
        Computer a = NewComputer("A");
        WriteEight(a);
        Assert.Equal(SyncOutcome.Synced, (await Sync(a)).Outcome);
        Computer b = NewComputer("B");
        WatchedStorage watched = new(b.Storage) { ObjectGets = new Gate(6) };
        b = b with { Storage = watched };

        SyncReport report = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal(6, watched.ObjectGets!.Max);
        foreach (string path in Eight) Assert.Equal(Read(a, path), Read(b, path));
    }

    [Fact(DisplayName = "Uploads run in parallel but the manifest is put last", Timeout = 30000)]
    public async Task UploadsInParallelManifestLast()
    {
        WatchedStorage watched = new(BoardSyncApi.FolderStorage(StorageFolder)) { ObjectPuts = new Gate(6) };
        Computer a = NewComputer("A", watched);
        WriteEight(a);

        SyncReport report = await Sync(a);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal(6, watched.ObjectPuts!.Max);
        Assert.Equal(9, watched.Puts.Count);
        Assert.Equal("manifest.json", watched.Puts[^1]);
        Assert.Equal(1, watched.Puts.Count(p => p == "manifest.json"));
    }

    [Fact(DisplayName = "A downloaded object is streamed to a temp file and never held whole in memory", Timeout = 30000)]
    public async Task DownloadsAreStreamedToATempFile()
    {
        Computer a = NewComputer("A");
        WriteEight(a);
        await Sync(a);
        Computer b = NewComputer("B");
        WatchedStorage watched = new(b.Storage, b.State);
        b = b with { Storage = watched };

        SyncReport report = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal(8, watched.ObjectGetToCalls);
        Assert.Equal(0, watched.ObjectGetCalls);                        // no byte[] came back from the storage for an object
        Assert.Equal(8, watched.StreamedToAFile.Count);
        Assert.All(watched.StreamedToAFile, onDisk => Assert.True(onDisk));
        foreach (string path in Eight) Assert.Equal(Read(a, path), Read(b, path));
        string fetch = Path.Combine(b.State, "fetch");
        Assert.True(!Directory.Exists(fetch) || !Directory.EnumerateFileSystemEntries(fetch).Any(), "the temp files are gone after the pass");
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(b)).Outcome);
    }

    [Fact(DisplayName = "An object whose hash the base folder already holds is not fetched", Timeout = 30000)]
    public async Task BaseCopyIsReused()
    {
        Computer a = NewComputer("A");
        Write(a, Tasks, SeedTasks);
        Write(a, "add-health-check/proposal.md", "# Add health check\n");
        await Sync(a);
        Computer b = NewComputer("B");
        await Sync(b);
        WatchedStorage watched = new(b.Storage, b.State);
        b = b with { Storage = watched };

        // Archived: the same content under a new path (the object key names the path, so it is a new object).
        File.Delete(Path.Combine(a.Root, Tasks));
        Write(a, "archive/tasks.md", SeedTasks);
        _now = _now.AddMinutes(5);
        Assert.Equal(SyncOutcome.Synced, (await Sync(a)).Outcome);

        SyncReport report = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal(0, watched.ObjectGetCalls + watched.ObjectGetToCalls);
        Assert.Equal(SeedTasks, Read(b, "archive/tasks.md"));
        Assert.False(File.Exists(Path.Combine(b.Root, Tasks)));
    }

    [Fact(DisplayName = "The manifest is written compact with three history hashes and an old twenty-entry manifest still reads")]
    public async Task CompactManifestShortHistory()
    {
        Computer a = NewComputer("A");
        Write(a, Tasks, SeedTasks);
        await Sync(a);
        for (int i = 1; i <= 25; i++)
        {
            _now = _now.AddMinutes(1);
            Write(a, Tasks, SeedTasks + $"version {i}\n");
            await Sync(a);
        }

        byte[] bytes = (await a.Storage.Get("manifest.json"))!.Bytes;
        string text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain("  ", text);
        Assert.Equal(3, JsonDocument.Parse(bytes).RootElement.GetProperty("entries").GetProperty(Tasks).GetProperty("history").GetArrayLength());

        // An older client wrote indented JSON with twenty hashes: it is read as before.
        string old = JsonSerializer.Serialize(new
        {
            version = 1,
            entries = new Dictionary<string, object>
            {
                [Tasks] = new { hash = new string('a', 64), clock = "000000000000001-0000-A", deleted = false, history = Enumerable.Range(0, 20).Select(i => new string((char)('b' + i % 5), 64)).ToArray() },
            },
        }, new JsonSerializerOptions { WriteIndented = true });
        ManifestFile parsed = SyncModels.ParseManifest(Encoding.UTF8.GetBytes(old));
        Assert.Equal(20, parsed.Entries[Tasks].History.Count);

        // A client that only knows the old reader reads the new text (same members, same names).
        ManifestFile again = SyncModels.ParseManifest(bytes);
        Assert.Equal(3, again.Entries[Tasks].History.Count);
    }

    [Fact(DisplayName = "A manifest entry with a base and a local edit but no remote entry fails clearly instead of crashing")]
    public async Task MissingRemoteEntryFailsClearly()
    {
        Computer a = NewComputer("A");
        Write(a, Tasks, SeedTasks);
        Write(a, "add-health-check/proposal.md", "# Add health check\n");
        await Sync(a);
        Computer b = NewComputer("B");
        await Sync(b);

        Write(b, Tasks, SeedTasks + "edited here\n");
        // The storage's manifest loses the entry (a damaged or hand-edited manifest) while this computer remembers it.
        ManifestFile manifest = SyncModels.ParseManifest((await a.Storage.Get("manifest.json"))!.Bytes);
        manifest.Entries.Remove(Tasks);
        await a.Storage.Put("manifest.json", SyncModels.Serialize(manifest), PutCondition.Always);

        SyncReport report = await Sync(b);

        Assert.Equal(SyncOutcome.Failed, report.Outcome);
        Assert.Contains(Tasks, report.Message!);
        Assert.DoesNotContain("NullReference", report.Message!);
        Assert.Contains("nothing was synced", report.Message!);
        Assert.Equal(SeedTasks + "edited here\n", Read(b, Tasks));
    }

    [Fact(DisplayName = "A stream of known length is read into one exact buffer, not buffered twice")]
    public async Task ReadCappedDoesNotCopyTwice()
    {
        byte[] content = new byte[4 * 1024 * 1024];
        new Random(7).NextBytes(content);
        using MemoryStream source = new(content);

        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] read = await StorageLimits.ReadCapped(source, long.MaxValue, "x", default);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(content, read);
        Assert.True(allocated < content.Length * 1.25, $"{allocated} bytes were allocated to read {content.Length}");
    }

    [Fact(DisplayName = "State files are read while another handle holds them open for writing, and written by rename")]
    public void StateFilesAreSharedAndRenamed()
    {
        string folder = Path.Combine(_temp.FullName, "state-files");
        Directory.CreateDirectory(folder);
        SyncStateStore store = new(folder);
        DateTime at = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
        string hash = new('a', 64);
        SyncStateFile first = new() { StorageVersion = "v1", Entries = new() { ["a.md"] = new ManifestEntry { Hash = hash, Clock = "000000000000001-0000-A" } } };
        store.Save(first, new Dictionary<string, Func<byte[]>>());
        store.SaveSeen(new() { ["a.md"] = [hash] });
        store.SavePending(["objects/ab/abc"]);
        store.SaveDeletions([new SyncStateStore.DeletionEntry(at, 1, 0, 5)]);
        store.SaveScan(new StatCacheFile());
        string[] names = ["state.json", "seen.json", "pending.json", "deletions.json", "scan.json"];
        List<FileStream> held = [.. names.Select(n => new FileStream(Path.Combine(folder, n), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))];
        try
        {
            Assert.Equal("v1", store.Load()!.StorageVersion);
            Assert.Single(store.LoadSeen());
            Assert.Single(store.LoadPending());
            Assert.Single(store.LoadDeletions(at.AddHours(1), TimeSpan.FromHours(24)));
            Assert.NotNull(store.LoadScan());
        }
        finally { foreach (FileStream stream in held) stream.Dispose(); }

        // Written by rename (a unique temp file, then a move over the target): nothing half-written is left behind. (Windows refuses a rename over a file that is open at that moment;
        // the write retries briefly, so a reader that holds the file for a moment only delays it.)
        SyncStateFile second = new() { StorageVersion = "v2", Entries = first.Entries };
        store.Save(second, new Dictionary<string, Func<byte[]>>());
        store.SaveSeen(new() { ["b.md"] = [hash] });
        store.SavePending(["objects/cd/cde"]);
        store.SaveDeletions([]);
        store.SaveScan(new StatCacheFile { ScannedAt = 5 });

        Assert.Empty(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(LocalTree.TempSuffix, StringComparison.Ordinal)));
        Assert.Equal("v2", store.Load()!.StorageVersion);
        Assert.Equal("b.md", store.LoadSeen().Keys.Single());
        Assert.Equal(["objects/cd/cde"], store.LoadPending());
        Assert.Equal(5, store.LoadScan()!.ScannedAt);
    }

    [Fact(DisplayName = "A folder storage streams a file into a stream and still refuses one over the cap")]
    public async Task FolderStorageStreams()
    {
        IBoardStorage storage = BoardSyncApi.FolderStorage(StorageFolder);
        await storage.Put("objects/ab/abc", new byte[100], PutCondition.Always);
        using MemoryStream into = new();

        Assert.True(await storage.GetTo("objects/ab/abc", into));
        Assert.Equal(100, into.Length);
        Assert.False(await storage.GetTo("objects/ab/missing", new MemoryStream()));
        using (StorageLimits.OverrideObjectMax(10))
        {
            BoardStorageException e = await Assert.ThrowsAsync<BoardStorageException>(() => storage.GetTo("objects/ab/abc", new MemoryStream()));
            Assert.True(e.IsTooLarge);
        }
    }
}

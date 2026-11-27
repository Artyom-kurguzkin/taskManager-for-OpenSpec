using System.Security.Cryptography;
using System.Text;
using BoardSync;
using BoardSync.PublicDTOs;
using BoardSync.Sync;

namespace TaskManager.Tests;

/// <summary>
/// Old content that git brings back is recognised without a ceiling: from the manifest's history, from this computer's own memory of every content it has seen
/// for a path, and from the storage itself (the upload of an object that is already there is refused). Objects are named by path and content together.
/// </summary>
public sealed class RecognisingOldContentTests : IDisposable
{
    private const string Dir = "add-health-check";
    private const string Yaml = Dir + "/.openspec.yaml";
    private const string Proposal = Dir + "/proposal.md";
    private const string Tasks = Dir + "/tasks.md";
    private const string Log = Dir + "/events.log";
    private const string Note = Dir + "/notes/n1.md";

    private const string SeedYaml =
        "schema: spec-driven\ncreated: 2026-11-01\nx-rtm:\n  id: 12\n  title: Add health check\n  status: Planned\n  priority: low\n  contributors: [Ann, Bob]\n";
    private const string SeedProposal = "# Add health check\n\n## Why\n\nWe cannot tell whether the service is up.\n";
    private const string SeedTasks = "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 write the test\n- [ ] 1.2 write the code\n";
    private const string SeedLog = "2026-11-10T10:00:00 | Ann | log | seeded\n";
    private const string SeedNote = "---\nid: 1\nby: Ann\ncreated: 2026-11-10T10:00:00\n---\n# First note\n\nWhat happened.\n";

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-sync-old-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private void Tick(int minutes) => _now = _now.AddMinutes(minutes);

    // ---- a computer ----------------------------------------------------------------------------------------------------------------------

    private sealed record Computer(string Id, string Root, string State, CountingStorage Storage);

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");

    private Computer NewComputer(string id, bool conditionalWrites = true) =>
        new(id, Path.Combine(_temp.FullName, id, "changes"), Path.Combine(_temp.FullName, id, "state"),
            new CountingStorage(BoardSyncApi.FolderStorage(StorageFolder)) { Conditional = conditionalWrites });

    private Task<SyncReport> Sync(Computer c, IReadOnlySet<string>? edited = null) =>
        BoardSyncApi.Sync(new SyncOptions(c.Root, c.State, c.Storage, c.Id, () => _now, EditedInApp: edited));

    private static IReadOnlySet<string> NoAppEdits => new HashSet<string>(StringComparer.Ordinal);
    private static IReadOnlySet<string> AppEdits(params string[] paths) => new HashSet<string>(paths, StringComparer.Ordinal);

    private void Write(Computer c, string path, string text)
    {
        string file = Path.Combine(c.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, _now);
    }

    private static string Read(Computer c, string path) => File.ReadAllText(Path.Combine(c.Root, path));

    private void SeedBoard(Computer c)
    {
        Write(c, Yaml, SeedYaml);
        Write(c, Proposal, SeedProposal);
        Write(c, Tasks, SeedTasks);
        Write(c, Log, SeedLog);
        Write(c, Note, SeedNote);
    }

    private async Task<(Computer A, Computer B)> TwoSyncedComputers(bool conditionalWritesAtB = true)
    {
        Computer a = NewComputer("A"), b = NewComputer("B", conditionalWritesAtB);
        SeedBoard(a);
        await Sync(a);
        await Sync(b);
        return (a, b);
    }

    /// <summary>The tasks text of version i; version 0 is the seed.</summary>
    private static string Version(int i) => i == 0 ? SeedTasks : SeedTasks + $"\n<!-- version {i} -->\n";

    /// <summary>Versions A writes so that the manifest, which keeps <see cref="SyncModels.HistoryLimit"/> earlier ones of a path, has forgotten versions 0 and 1 (each version is one sync: no more than that is written).</summary>
    private const int Enough = SyncModels.HistoryLimit + 2;   // the first two are out of the history after that many

    /// <summary>A writes versions 1..count of the tasks, syncing after each, so the manifest's history is long gone past version 0 and 1.</summary>
    private async Task Evolve(Computer a, int count)
    {
        for (int i = 1; i <= count; i++)
        {
            Tick(1);
            Write(a, Tasks, Version(i));
            await Sync(a);
        }
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>The key of an object: the hash of the path, a zero byte and the content hash.</summary>
    private static string ObjectKey(string path, string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path + "\0" + Sha(content)))).ToLowerInvariant();

    private string ObjectFile(string path, string content)
    {
        string key = ObjectKey(path, content);
        return Path.Combine(StorageFolder, "objects", key[..2], key);
    }

    private static string ManifestHash(IBoardStorage storage, string path) =>
        System.Text.Json.JsonDocument.Parse(storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes).RootElement
            .GetProperty("entries").GetProperty(path).GetProperty("hash").GetString()!;

    private static SortedDictionary<string, string> Tree(Computer c) =>
        new(Directory.EnumerateFiles(c.Root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(c.Root, f).Replace('\\', '/'), File.ReadAllText));

    private sealed class CountingStorage(IBoardStorage inner) : IBoardStorage
    {
        public int Calls { get; private set; }
        public int Puts { get; private set; }
        public bool Conditional { get; init; } = true;
        /// <summary>Throw a storage failure on the next put of the manifest (the process "dies" after the objects went up).</summary>
        public bool FailNextManifestPut { get; set; }

        public bool ConditionalWrites => Conditional && inner.ConditionalWrites;

        public Task<StoredFile?> Get(string path, CancellationToken ct = default) { Calls++; return inner.Get(path, ct); }
        public Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default) { Calls++; return inner.GetIfChanged(path, version, ct); }

        public Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default)
        {
            Calls++;
            Puts++;
            if (path == "manifest.json" && FailNextManifestPut)
            {
                FailNextManifestPut = false;
                throw new BoardStorageException("The storage went away.");
            }
            return inner.Put(path, bytes, condition, ct);
        }

        public Task Delete(string path, CancellationToken ct = default) { Calls++; return inner.Delete(path, ct); }
        public Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default) { Calls++; return inner.List(prefix, ct); }
    }

    // ---- the facts -----------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Content older than the manifest remembers is still recognised as stale from this computer's own memory")]
    public async Task OwnMemoryRecognisesVeryOldContent()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        await Evolve(a, Enough);                                      // the manifest keeps a few earlier versions: version 0 is long gone from it
        await Sync(b);
        Tick(1);
        Write(b, Tasks, Version(0));                                  // a week-old branch is checked out
        int puts = b.Storage.Puts;

        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal([Tasks], report.Stale);
        Assert.Equal([Tasks], report.Downloaded);
        Assert.Empty(report.Uploaded);
        Assert.Equal(Version(Enough), Read(b, Tasks));
        Assert.Equal(puts, b.Storage.Puts);                           // recognised here, nothing was even offered to the storage
        Assert.Equal(Sha(Version(Enough)), ManifestHash(a.Storage, Tasks));
    }

    [Fact(DisplayName = "Content this computer never saw but the storage has had for that file is recognised when the upload is refused, with no extra request")]
    public async Task RefusedUploadMeansStale()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        Computer c = NewComputer("C");
        await Sync(c);
        await Evolve(a, Enough);
        await Sync(b);
        await Sync(c);                                                // both know version 0 and the last one and the few before it, never version 1
        Tick(1);
        Write(b, Tasks, Version(1));                                  // content the storage had once (a put of its object is refused)
        Write(c, Tasks, SeedTasks + "\n<!-- written by a commit nobody has seen -->\n");
        int callsB = b.Storage.Calls, putsB = b.Storage.Puts, callsC = c.Storage.Calls;

        SyncReport stale = await Sync(b, NoAppEdits);
        string afterStale = ManifestHash(a.Storage, Tasks);
        SyncReport real = await Sync(c, NoAppEdits);

        Assert.Equal([Tasks], stale.Stale);
        Assert.Equal([Tasks], stale.Downloaded);
        Assert.Empty(stale.Uploaded);
        Assert.Equal(Version(Enough), Read(b, Tasks));
        Assert.Equal(1, b.Storage.Puts - putsB);                      // the one put of the object, which was refused; the manifest was not touched
        Assert.Equal(Sha(Version(Enough)), afterStale);
        Assert.Equal([Tasks], real.Uploaded);                         // the same kind of sync of a genuinely new file...
        Assert.Empty(real.Stale);
        Assert.True(b.Storage.Calls - callsB <= c.Storage.Calls - callsC, $"stale {b.Storage.Calls - callsB} calls, new {c.Storage.Calls - callsC}");   // ...costs at least as many requests
    }

    [Fact(DisplayName = "The same text in two different files is two objects: one file's old text is not mistaken for the other's")]
    public async Task SameTextInAnotherFileIsNotStale()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        Tick(1);
        Write(a, Proposal, SeedTasks);                                // the storage has had this text, for the proposal
        await Sync(a);
        Tick(1);
        Write(a, Proposal, SeedProposal + "\nmore\n");
        await Sync(a);
        await Sync(b);
        Tick(1);
        Write(b, Tasks, SeedTasks + "\n<!-- unrelated -->\n");
        Write(b, Note, SeedTasks);                                    // the text of the proposal's old version, in a file that never held it

        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal(Sorted([Tasks, Note]), Sorted(report.Uploaded));
        Assert.Empty(report.Stale);
        Assert.True(File.Exists(ObjectFile(Note, SeedTasks)));
        Assert.True(File.Exists(ObjectFile(Proposal, SeedTasks)));
        Assert.NotEqual(ObjectFile(Note, SeedTasks), ObjectFile(Proposal, SeedTasks));
        Assert.Equal(SeedTasks, Read(b, Note));
    }

    [Fact(DisplayName = "A real outside edit whose upload was interrupted before the manifest changed is still an edit on the next sync")]
    public async Task InterruptedUploadOfARealEditIsNotStale()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        string fresh = SeedTasks + "\n<!-- a commit nobody has seen -->\n";
        Tick(1);
        Write(b, Tasks, fresh);
        b.Storage.FailNextManifestPut = true;

        SyncReport died = await Sync(b, NoAppEdits);                  // the object is up, the manifest is not replaced

        Assert.Equal(SyncOutcome.Failed, died.Outcome);
        Assert.True(File.Exists(ObjectFile(Tasks, fresh)));
        Assert.True(File.Exists(Path.Combine(b.State, "pending.json")));
        Assert.Equal(fresh, Read(b, Tasks));

        Tick(1);
        SyncReport again = await Sync(b, NoAppEdits);

        Assert.Equal([Tasks], again.Uploaded);
        Assert.Empty(again.Stale);
        Assert.Equal(fresh, Read(b, Tasks));
        Assert.Equal(Sha(fresh), ManifestHash(a.Storage, Tasks));
        Assert.False(File.Exists(Path.Combine(b.State, "pending.json")));
        Assert.Equal([Tasks], (await Sync(a)).Downloaded);
    }

    [Fact(DisplayName = "An app edit is uploaded even when the storage has had that content before")]
    public async Task AppEditIsNeverStale()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        await Evolve(a, Enough);
        await Sync(b);
        Tick(1);
        Write(b, Tasks, Version(1));                                  // the user typed this in the app; the storage had it a long time ago

        SyncReport report = await Sync(b, AppEdits(Tasks));

        Assert.Equal([Tasks], report.Uploaded);
        Assert.Empty(report.Stale);
        Assert.Equal(Version(1), Read(b, Tasks));
        Assert.Equal(Sha(Version(1)), ManifestHash(a.Storage, Tasks));
        Assert.Equal([Tasks], (await Sync(a)).Downloaded);
        Assert.Equal(Version(1), Read(a, Tasks));
    }

    [Fact(DisplayName = "An old event log that a checkout brings back is merged with the storage's log and removes no line")]
    public async Task OldEventLogIsMerged()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        string later = SeedLog + "2026-11-12T08:05:00 | Ann | log | one\n2026-11-12T08:06:00 | Ann | log | two\n";
        Tick(5);
        Write(a, Log, later);
        await Sync(a);
        await Sync(b);
        Tick(5);
        Write(b, Log, SeedLog);                                       // the log of an old commit
        int puts = b.Storage.Puts;

        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal([Log], report.Merged);
        Assert.Empty(report.Stale);
        Assert.Empty(report.Uploaded);
        Assert.Equal(later, Read(b, Log));
        Assert.Equal(puts, b.Storage.Puts);                           // the storage's log already had every line

        Tick(5);
        string withNew = later + "2026-11-12T08:10:00 | Bob | log | from a pulled commit\n";
        Write(b, Log, SeedLog + "2026-11-12T08:10:00 | Bob | log | from a pulled commit\n");    // an old log plus one line of its own
        SyncReport both = await Sync(b, NoAppEdits);
        await Sync(a);

        Assert.Empty(both.Stale);
        Assert.Equal(withNew, Read(b, Log));
        Assert.Equal(withNew, Read(a, Log));
    }

    [Fact(DisplayName = "A moved file is stored again under its new path and both computers end with the same content")]
    public async Task MovedFileIsStoredAgain()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        const string Moved = "archive/2026-11-12-" + Proposal;
        Tick(5);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(a.Root, Moved))!);
        File.Move(Path.Combine(a.Root, Proposal), Path.Combine(a.Root, Moved));

        SyncReport up = await Sync(a);
        SyncReport down = await Sync(b);

        // A file moved by hand out of a folder that stays is not a folder's move: nothing says the old path was removed on purpose, so it is put back (nothing is deleted),
        // and the new path is a new file that is stored under its own object.
        Assert.Equal([Moved], up.Uploaded);
        Assert.Equal([Proposal], up.Downloaded);
        Assert.Empty(up.DeletedInStorage);
        Assert.Equal([Moved], down.Downloaded);
        Assert.Empty(down.DeletedHere);
        Assert.Equal(SeedProposal, Read(a, Proposal));
        Assert.Equal(SeedProposal, Read(b, Proposal));
        Assert.Equal(Tree(a), Tree(b));
        Assert.Equal(SeedProposal, Read(b, Moved));
        Assert.True(File.Exists(ObjectFile(Moved, SeedProposal)));
        Assert.True(File.Exists(ObjectFile(Proposal, SeedProposal)));
        Assert.NotEqual(ObjectFile(Moved, SeedProposal), ObjectFile(Proposal, SeedProposal));
        Assert.False(File.Exists(Path.Combine(StorageFolder, "objects", Sha(SeedProposal)[..2], Sha(SeedProposal))));    // never named by content alone
    }

    [Fact(DisplayName = "With a storage that cannot do conditional writes only this computer's memory is used")]
    public async Task WithoutConditionalWritesOnlyOwnMemoryIsUsed()
    {
        (Computer a, Computer b) = await TwoSyncedComputers(conditionalWritesAtB: false);
        await Evolve(a, Enough);
        await Sync(b);
        Tick(1);
        Write(b, Tasks, Version(1));                                  // the storage had it, this computer never saw it: not recognisable without the refusal

        SyncReport unseen = await Sync(b, NoAppEdits);

        Assert.Equal([Tasks], unseen.Uploaded);
        Assert.Empty(unseen.Stale);

        Tick(1);
        Write(b, Tasks, Version(0));                                  // this computer saw version 0 at its first sync

        SyncReport seen = await Sync(b, NoAppEdits);

        Assert.Equal([Tasks], seen.Stale);
        Assert.Equal(Version(1), Read(b, Tasks));
        Assert.Equal(Sha(Version(1)), ManifestHash(a.Storage, Tasks));
    }

    [Fact(DisplayName = "A file that comes back through git after it was deleted, with content the storage had long ago, is deleted again")]
    public async Task OldContentOfADeletedFileIsRecognisedByTheRefusal()
    {
        // A file is only ever removed for a reason the item's log gives (a plain deletion is put back), so this one is a delta spec the app removed: its log says "spec-removed".
        const string Spec = Dir + "/specs/health/spec.md";
        static string SpecVersion(int i) => $"## ADDED Requirements\n\n### Requirement: Health\n\nThe service SHALL answer.\n\n<!-- version {i} -->\n";
        (Computer a, Computer b) = await TwoSyncedComputers();
        Write(a, Spec, SpecVersion(0));
        await Sync(a);
        await Sync(b);
        for (int i = 1; i <= Enough; i++)
        {
            Tick(1);
            Write(a, Spec, SpecVersion(i));
            await Sync(a);
        }
        Tick(1);
        File.Delete(Path.Combine(a.Root, Spec));
        Write(a, Log, Read(a, Log) + "2026-11-12T09:00:00 | Ann | spec-removed | specs/health/spec.md\n");
        await Sync(a);
        await Sync(b);                                                // the spec is deleted on both computers, for the reason the log gives
        Assert.False(File.Exists(Path.Combine(b.Root, Spec)));
        Tick(1);
        Write(b, Spec, SpecVersion(3));                               // an old branch has it again, with a version nobody here saw

        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal([Spec], report.Stale);
        Assert.Equal([Spec], report.DeletedHere);
        Assert.Empty(report.Uploaded);
        Assert.False(File.Exists(Path.Combine(b.Root, Spec)));
        Assert.Single(report.Trashed);
        Assert.Equal(SpecVersion(3), File.ReadAllText(report.Trashed[0]));
        System.Text.Json.JsonElement entry = System.Text.Json.JsonDocument.Parse(a.Storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes).RootElement.GetProperty("entries").GetProperty(Spec);
        Assert.True(entry.GetProperty("deleted").GetBoolean());
    }

    private static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal);
}

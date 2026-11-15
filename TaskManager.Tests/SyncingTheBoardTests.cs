using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;
using BoardSync.Sync;

namespace TaskManager.Tests;

/// <summary>
/// The sync loop: two computers (two directories, two state folders) and one shared storage (a temp folder), with a clock the test advances by hand.
/// Paths are relative to the synced directory (openspec/changes in the app).
/// </summary>
public sealed class SyncingTheBoardTests : IDisposable
{
    private const string Dir = "add-health-check";
    private const string Yaml = Dir + "/.openspec.yaml";
    private const string Proposal = Dir + "/proposal.md";
    private const string Tasks = Dir + "/tasks.md";
    private const string Log = Dir + "/events.log";
    private const string Note = Dir + "/notes/n1.md";
    private static readonly string[] Seeded = [Yaml, Proposal, Tasks, Log, Note];

    private const string SeedYaml =
        "schema: spec-driven\ncreated: 2026-11-01\nx-rtm:\n  id: 12\n  title: Add health check\n  status: Planned\n  priority: low\n  contributors: [Ann, Bob]\n";
    private const string SeedProposal = "# Add health check\n\n## Why\n\nWe cannot tell whether the service is up.\n";
    private const string SeedTasks = "# Tasks\n\n## 1. Tasks\n\n- [ ] 1.1 write the test\n- [ ] 1.2 write the code\n";
    private const string SeedLog = "2026-11-10T10:00:00 | Ann | log | seeded\n";
    private const string SeedNote = "---\nid: 1\nby: Ann\ncreated: 2026-11-10T10:00:00\n---\n# First note\n\nWhat happened.\n";

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-sync-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private void Tick(int minutes) => _now = _now.AddMinutes(minutes);

    // ---- a computer ----------------------------------------------------------------------------------------------------------------------

    private sealed record Computer(string Id, string Root, string State, IBoardStorage Storage);

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");
    private SpyStorage NewSpy() => new(BoardSyncApi.FolderStorage(StorageFolder));

    private Computer NewComputer(string id, IBoardStorage? storage = null) =>
        new(id, Path.Combine(_temp.FullName, id, "changes"), Path.Combine(_temp.FullName, id, "state"), storage ?? BoardSyncApi.FolderStorage(StorageFolder));

    private Task<SyncReport> Sync(Computer c, bool allowMassDelete = false, SyncHooks? hooks = null)
    {
        SyncOptions options = new(c.Root, c.State, c.Storage, c.Id, () => _now, allowMassDelete);
        return hooks is null ? BoardSyncApi.Sync(options) : SyncEngine.Run(options, hooks, default);
    }

    private void Write(Computer c, string path, string text)
    {
        string file = Path.Combine(c.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, _now);
    }

    private static string Read(Computer c, string path) => File.ReadAllText(Path.Combine(c.Root, path));
    private static bool Exists(Computer c, string path) => File.Exists(Path.Combine(c.Root, path));
    private static void Delete(Computer c, string path) => File.Delete(Path.Combine(c.Root, path));

    private void SeedBoard(Computer c)
    {
        Write(c, Yaml, SeedYaml);
        Write(c, Proposal, SeedProposal);
        Write(c, Tasks, SeedTasks);
        Write(c, Log, SeedLog);
        Write(c, Note, SeedNote);
    }

    /// <summary>Both computers hold the seeded board and have synced it.</summary>
    private async Task<(Computer A, Computer B)> TwoSyncedComputers(IBoardStorage? storageA = null, IBoardStorage? storageB = null)
    {
        Computer a = NewComputer("A", storageA), b = NewComputer("B", storageB);
        SeedBoard(a);
        await Sync(a);
        await Sync(b);
        return (a, b);
    }

    private static SortedDictionary<string, string> Tree(Computer c) =>
        new(Directory.Exists(c.Root)
            ? Directory.EnumerateFiles(c.Root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(c.Root, f).Replace('\\', '/'), File.ReadAllText)
            : []);

    /// <summary>Every file under the folders with its size and last-write time: equal snapshots mean nothing was written.</summary>
    private static List<string> Snapshot(params string[] folders) =>
        [.. folders.Where(Directory.Exists).SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
            .Where(f => Path.GetFileName(f) != "scan.json")      // the stat cache is a speed-up the passes keep, not state or content
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"{f}|{new FileInfo(f).Length}|{File.GetLastWriteTimeUtc(f).Ticks}")];

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal);

    private static JsonElement ManifestEntries(IBoardStorage storage) =>
        JsonDocument.Parse(storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes).RootElement.GetProperty("entries").Clone();

    // ---- a storage that can be watched, interrupted and made to fail -------------------------------------------------------------

    private sealed class SpyStorage(IBoardStorage inner) : IBoardStorage
    {
        public int Writes { get; private set; }
        public int ManifestPuts { get; private set; }
        /// <summary>Runs once, right after the manifest was read and before the caller sees it: another computer syncs "at the same moment".</summary>
        public Func<Task>? AfterManifestRead { get; set; }
        /// <summary>(operation, path) → throw a storage failure.</summary>
        public Func<string, string, bool>? FailWhen { get; set; }

        public bool ConditionalWrites => inner.ConditionalWrites;

        private void Check(string operation, string path)
        {
            if (FailWhen?.Invoke(operation, path) == true) throw new BoardStorageException($"The storage is unreachable ({operation} {path}).");
        }

        private async Task<StoredFile?> Seen(string path, StoredFile? file)
        {
            if (path == "manifest.json" && AfterManifestRead is { } hook)
            {
                AfterManifestRead = null;
                await hook();
            }
            return file;
        }

        public async Task<StoredFile?> Get(string path, CancellationToken ct = default)
        {
            Check("get", path);
            return await Seen(path, await inner.Get(path, ct));
        }

        public async Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default)
        {
            Check("getIfChanged", path);
            return await Seen(path, await inner.GetIfChanged(path, version, ct));
        }

        public Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default)
        {
            Check("put", path);
            Writes++;
            if (path == "manifest.json") ManifestPuts++;
            return inner.Put(path, bytes, condition, ct);
        }

        public Task Delete(string path, CancellationToken ct = default)
        {
            Check("delete", path);
            Writes++;
            return inner.Delete(path, ct);
        }

        public Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default)
        {
            Check("list", prefix);
            return inner.List(prefix, ct);
        }
    }

    // ---- the facts -----------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "The first computer to connect uploads its board, and a second computer with an empty folder receives all of it")]
    public async Task FirstConnection()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        SeedBoard(a);

        SyncReport first = await Sync(a);
        SyncReport second = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, first.Outcome);
        Assert.Equal(Sorted(Seeded), Sorted(first.Uploaded));
        Assert.Empty(first.Downloaded);
        Assert.Equal(SyncOutcome.Synced, second.Outcome);
        Assert.Equal(Sorted(Seeded), Sorted(second.Downloaded));
        Assert.Empty(second.Uploaded);
        Assert.Equal(Tree(a), Tree(b));
        Assert.Equal(SeedNote, Read(b, Note));
        string key = Sha(Tasks + "\0" + Sha(SeedTasks));   // an object is named by its path and its content
        Assert.True(File.Exists(Path.Combine(StorageFolder, "objects", key[..2], key)));
        Assert.Equal(Sha(SeedTasks), ManifestEntries(a.Storage).GetProperty(Tasks).GetProperty("hash").GetString());
        Assert.False(ManifestEntries(a.Storage).GetProperty(Tasks).GetProperty("deleted").GetBoolean());
    }

    [Fact(DisplayName = "A change on one computer reaches the other on its next sync, and a sync with nothing new writes nothing anywhere")]
    public async Task ChangeTravelsAndQuietSyncWritesNothing()
    {
        SpyStorage spyA = NewSpy(), spyB = NewSpy();
        (Computer a, Computer b) = await TwoSyncedComputers(spyA, spyB);

        Tick(5);
        Write(a, Tasks, SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1"));
        SyncReport up = await Sync(a);
        SyncReport down = await Sync(b);

        Assert.Equal([Tasks], up.Uploaded);
        Assert.Equal([Tasks], down.Downloaded);
        Assert.Equal(Tree(a), Tree(b));
        Assert.Contains("- [x] 1.1", Read(b, Tasks));

        Tick(5);
        List<string> before = Snapshot(a.Root, a.State, b.Root, b.State, StorageFolder);
        int writesA = spyA.Writes, writesB = spyB.Writes;
        SyncReport quietA = await Sync(a);
        SyncReport quietB = await Sync(b);

        Assert.Equal(SyncOutcome.NothingToDo, quietA.Outcome);
        Assert.Equal(SyncOutcome.NothingToDo, quietB.Outcome);
        Assert.Equal((writesA, writesB), (spyA.Writes, spyB.Writes));
        Assert.Equal(before, Snapshot(a.Root, a.State, b.Root, b.State, StorageFolder));
    }

    [Fact(DisplayName = "Files changed on both computers while apart are merged by their rules, and both computers end with the same content")]
    public async Task BothChangedAreMerged()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Yaml, SeedYaml.Replace("status: Planned", "status: In Progress"));
        Write(a, Log, SeedLog + "2026-11-12T08:05:00 | Ann | status | Planned -> In Progress\n");
        Write(a, Proposal, SeedProposal + "\nAnn added a paragraph.\n");
        Tick(5);
        Write(b, Yaml, SeedYaml.Replace("priority: low", "priority: high"));
        Write(b, Log, SeedLog + "2026-11-12T08:08:00 | Bob | log | raised the priority\n");
        await Sync(a);
        SyncReport atB = await Sync(b);
        SyncReport back = await Sync(a);

        Assert.Equal(SyncOutcome.Synced, atB.Outcome);
        Assert.Equal(Sorted([Yaml, Log]), Sorted(atB.Merged));
        Assert.Equal([Proposal], atB.Downloaded);
        Assert.Empty(atB.Conflicts);
        Assert.Equal(Sorted([Yaml, Log]), Sorted(back.Downloaded));      // A already has its own proposal edit
        Assert.Equal(Tree(a), Tree(b));
        Assert.Contains("status: In Progress", Read(b, Yaml));
        Assert.Contains("priority: high", Read(b, Yaml));
        Assert.Contains("Ann added a paragraph.", Read(b, Proposal));
        Assert.Contains("Planned -> In Progress", Read(a, Log));
        Assert.Contains("raised the priority", Read(a, Log));
    }

    [Fact(DisplayName = "A field changed on both computers goes to the later change and the sync reports what was dropped")]
    public async Task SameFieldGoesToTheLaterChange()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Yaml, SeedYaml.Replace("status: Planned", "status: In Progress"));
        Tick(5);
        Write(b, Yaml, SeedYaml.Replace("status: Planned", "status: In Review"));
        await Sync(a);
        SyncReport atB = await Sync(b);
        SyncReport back = await Sync(a);

        SyncConflict conflict = Assert.Single(atB.Conflicts);
        Assert.Equal((Yaml, "status", "In Review", "In Progress"), (conflict.Path, conflict.What, conflict.Kept, conflict.Dropped));
        Assert.Equal([Yaml], atB.Merged);
        Assert.Contains("status: In Review", Read(b, Yaml));
        Assert.Equal([Yaml], back.Downloaded);
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A conflict says whether this computer's change or another computer's was dropped")]
    public async Task AConflictSaysWhichSideWasDropped()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        // Another computer's change is the later one and is already in the storage: this computer's is dropped.
        Tick(5);
        Write(a, Yaml, SeedYaml.Replace("status: Planned", "status: In Progress"));
        Tick(5);
        Write(b, Yaml, SeedYaml.Replace("status: Planned", "status: In Review"));
        await Sync(b);
        SyncConflict here = Assert.Single((await Sync(a)).Conflicts);
        Assert.Equal(("In Review", "In Progress", true), (here.Kept, here.Dropped, here.DroppedHere));
        await Sync(b);

        // This computer's change is the later one: the other computer's version is dropped.
        Tick(5);
        Write(a, Yaml, Read(a, Yaml).Replace("status: In Review", "status: Blocked"));
        Tick(5);
        Write(b, Yaml, Read(b, Yaml).Replace("status: In Review", "status: Completed"));
        await Sync(a);
        SyncConflict there = Assert.Single((await Sync(b)).Conflicts);
        Assert.Equal(("Completed", "Blocked", false), (there.Kept, there.Dropped, there.DroppedHere));
        await Sync(a);

        // A text merge says the same: the dropped paragraph was the other computer's.
        Tick(5);
        Write(a, Proposal, "# Add health check\n\n## Why\n\nAlpha reason.\n");
        Tick(5);
        Write(b, Proposal, "# Add health check\n\n## Why\n\nBeta reason.\n");
        await Sync(a);
        SyncConflict text = Assert.Single((await Sync(b)).Conflicts);
        Assert.Contains("Alpha reason.", text.Dropped);
        Assert.False(text.DroppedHere);

        // A delete that wins over an edit here drops this computer's text; an edit dropped by this computer's delete was another computer's.
        Tick(10);
        string edited = Read(b, Note) + "\nAn edit that will lose.\n";
        Write(b, Note, edited);
        Tick(10);
        Delete(a, Note);
        await Sync(a);
        SyncConflict lostHere = Assert.Single((await Sync(b)).Conflicts);
        Assert.Equal((edited, true), (lostHere.Dropped, lostHere.DroppedHere));

        Tick(10);
        Write(b, Tasks, SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1"));
        await Sync(b);
        Tick(10);
        Delete(a, Tasks);
        SyncConflict lostThere = Assert.Single((await Sync(a)).Conflicts);
        Assert.Contains("[x] 1.1", lostThere.Dropped);
        Assert.False(lostThere.DroppedHere);
        Assert.False(new SyncConflict("x", "y", "z", "w").DroppedHere);                  // existing constructions keep compiling
    }

    [Fact(DisplayName = "A file deleted on one computer is deleted on the other, unless it was edited there later: then it comes back")]
    public async Task DeleteReachesTheOtherUnlessEditedLater()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(10);
        Delete(a, Note);
        Delete(a, Proposal);
        SyncReport deleted = await Sync(a);
        Tick(10);
        Write(b, Proposal, SeedProposal + "\nBob kept working on it.\n");
        SyncReport atB = await Sync(b);
        SyncReport back = await Sync(a);

        Assert.Equal(Sorted([Note, Proposal]), Sorted(deleted.DeletedInStorage));
        Assert.Equal([Note], atB.DeletedHere);
        Assert.Equal([Proposal], atB.Uploaded);
        Assert.Empty(atB.Conflicts);
        Assert.False(Exists(b, Note));
        Assert.False(Directory.Exists(Path.Combine(b.Root, Dir, "notes")));    // the emptied folder goes too
        Assert.True(Directory.Exists(b.Root));
        Assert.Equal([Proposal], back.Downloaded);
        Assert.Equal(SeedProposal + "\nBob kept working on it.\n", Read(a, Proposal));
        Assert.False(Exists(a, Note));
        Assert.Equal(Tree(a), Tree(b));
        Assert.False(ManifestEntries(a.Storage).GetProperty(Proposal).GetProperty("deleted").GetBoolean());
        Assert.True(ManifestEntries(a.Storage).GetProperty(Note).GetProperty("deleted").GetBoolean());
    }

    [Fact(DisplayName = "An edit that loses to a later delete is reported with its text")]
    public async Task EditLosingToDeleteIsReported()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        // B edits first and has not synced; A deletes later and syncs.
        Tick(10);
        string edited = SeedProposal + "\nAn edit that will lose.\n";
        Write(b, Proposal, edited);
        Tick(10);
        Delete(a, Proposal);
        await Sync(a);
        SyncReport atB = await Sync(b);

        Assert.Equal([Proposal], atB.DeletedHere);
        SyncConflict conflict = Assert.Single(atB.Conflicts);
        Assert.Equal(Proposal, conflict.Path);
        Assert.Equal(edited, conflict.Dropped);
        Assert.False(Exists(b, Proposal));

        // The other way round: B edited and synced; A deleted later without having synced.
        Tick(10);
        string editedYaml = SeedYaml.Replace("priority: low", "priority: high");
        Write(b, Yaml, editedYaml);
        await Sync(b);
        Tick(10);
        Delete(a, Yaml);
        SyncReport atA = await Sync(a);

        Assert.Equal([Yaml], atA.DeletedInStorage);
        SyncConflict lost = Assert.Single(atA.Conflicts);
        Assert.Equal((Yaml, editedYaml), (lost.Path, lost.Dropped));
        Assert.False(Exists(a, Yaml));
    }

    [Fact(DisplayName = "Two computers syncing at the same moment both succeed: the one that lost the manifest retries against the winner's")]
    public async Task SimultaneousSyncsBothSucceed()
    {
        SpyStorage spyA = NewSpy();
        (Computer a, Computer b) = await TwoSyncedComputers(spyA);

        Tick(5);
        Write(a, Tasks, SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1"));
        Write(a, Log, SeedLog + "2026-11-12T08:05:00 | Ann | log | from A\n");
        Write(b, Proposal, SeedProposal + "\nBob's paragraph.\n");
        Write(b, Log, SeedLog + "2026-11-12T08:06:00 | Bob | log | from B\n");
        SyncReport? atB = null;
        int putsBefore = spyA.ManifestPuts;
        spyA.AfterManifestRead = async () => atB = await Sync(b);       // B's whole sync lands between A's manifest read and A's manifest put

        SyncReport atA = await Sync(a);

        Assert.Equal(SyncOutcome.Synced, atB!.Outcome);
        Assert.Equal(SyncOutcome.Synced, atA.Outcome);
        Assert.Equal(2, spyA.ManifestPuts - putsBefore);                             // the first put was refused, the second went through
        Assert.Equal([Tasks], atA.Uploaded);
        Assert.Equal([Proposal], atA.Downloaded);
        Assert.Equal([Log], atA.Merged);

        await Sync(b);
        Assert.Equal(Tree(a), Tree(b));
        Assert.Contains("- [x] 1.1", Read(b, Tasks));
        Assert.Contains("Bob's paragraph.", Read(a, Proposal));
        Assert.Contains("from A", Read(b, Log));
        Assert.Contains("from B", Read(a, Log));
    }

    [Fact(DisplayName = "A second computer that already has a different copy takes the storage's version and its own copy is backed up and listed")]
    public async Task FirstConnectionWithADifferentCopy()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        SeedBoard(a);
        await Sync(a);
        string bOwnTasks = SeedTasks.Replace("write the code", "write the code, differently");
        Write(b, Tasks, bOwnTasks);
        Write(b, Proposal, SeedProposal);                                // identical: only recorded
        Write(b, "only-on-b/proposal.md", "# Only here\n");              // only here: uploaded

        Tick(1);
        SyncReport atB = await Sync(b);

        string backup = Assert.Single(atB.BackedUp);
        Assert.StartsWith(Path.Combine(b.State, "backup") + Path.DirectorySeparatorChar, Path.GetFullPath(backup));
        Assert.EndsWith(Path.Combine(Dir, "tasks.md"), backup);
        Assert.Equal(bOwnTasks, File.ReadAllText(backup));
        Assert.Equal(SeedTasks, Read(b, Tasks));
        Assert.Contains(Tasks, atB.Downloaded);
        Assert.DoesNotContain(Proposal, atB.Downloaded.Concat(atB.Uploaded));
        Assert.Equal(["only-on-b/proposal.md"], atB.Uploaded);
        Assert.Empty(atB.Merged);
        Assert.Equal(Sorted([Yaml, Log, Note, Tasks]), Sorted(atB.Downloaded));

        await Sync(a);
        Assert.Equal("# Only here\n", Read(a, "only-on-b/proposal.md"));
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A sync that would delete a large share of the board is blocked and changes nothing until it is allowed")]
    public async Task MassDeleteIsBlocked()
    {
        SpyStorage spyA = NewSpy(), spyB = NewSpy();
        Computer a = NewComputer("A", spyA), b = NewComputer("B", spyB);
        SeedBoard(a);
        for (int i = 2; i <= 13; i++) Write(a, $"big-change/notes/n{i}.md", $"# Note {i}\n\nText {i}.\n");   // 17 files in all
        await Sync(a);
        await Sync(b);

        Tick(5);
        for (int i = 2; i <= 12; i++) Delete(a, $"big-change/notes/n{i}.md");                              // 11 of 17
        List<string> before = Snapshot(a.Root, a.State, StorageFolder);
        int writes = spyA.Writes;
        SyncReport blocked = await Sync(a);

        Assert.Equal(SyncOutcome.Blocked, blocked.Outcome);
        Assert.Contains("11 of 17", blocked.Message);
        Assert.Equal(writes, spyA.Writes);
        Assert.Equal(before, Snapshot(a.Root, a.State, StorageFolder));
        Assert.Empty(blocked.Uploaded.Concat(blocked.DeletedInStorage));

        SyncReport allowed = await Sync(a, allowMassDelete: true);

        Assert.Equal(SyncOutcome.Synced, allowed.Outcome);
        Assert.Equal(11, allowed.DeletedInStorage.Count);

        // The same brake on the receiving side.
        List<string> beforeB = Snapshot(b.Root, b.State);
        SyncReport blockedB = await Sync(b);
        Assert.Equal(SyncOutcome.Blocked, blockedB.Outcome);
        Assert.Contains("11 of 17", blockedB.Message);
        Assert.Equal(beforeB, Snapshot(b.Root, b.State));
        Assert.True(Exists(b, "big-change/notes/n2.md"));

        SyncReport allowedB = await Sync(b, allowMassDelete: true);
        Assert.Equal(SyncOutcome.Synced, allowedB.Outcome);
        Assert.Equal(11, allowedB.DeletedHere.Count);
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A storage failure fails the sync with a message and leaves the local files and the sync state untouched")]
    public async Task StorageFailureChangesNothingLocally()
    {
        SpyStorage spyB = NewSpy();
        (Computer a, Computer b) = await TwoSyncedComputers(storageB: spyB);
        Tick(5);
        Write(a, Tasks, SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1"));
        await Sync(a);                                                   // the storage holds a change B has not seen
        Tick(5);
        Write(b, Proposal, SeedProposal + "\nBob's paragraph.\n");
        List<string> before = Snapshot(b.Root, b.State);

        spyB.FailWhen = (_, _) => true;
        SyncReport unreachable = await Sync(b);
        spyB.FailWhen = (op, path) => op == "put" && path == "manifest.json";    // fails after the objects went up, when there is a download waiting
        SyncReport refused = await Sync(b);

        Assert.Equal(SyncOutcome.Failed, unreachable.Outcome);
        Assert.Contains("The storage is unreachable", unreachable.Message);
        Assert.Equal(SyncOutcome.Failed, refused.Outcome);
        Assert.Contains("The storage is unreachable (put manifest.json)", refused.Message);
        Assert.Equal(before, Snapshot(b.Root, b.State));
        Assert.Equal(SeedTasks, Read(b, Tasks));

        spyB.FailWhen = null;
        SyncReport recovered = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, recovered.Outcome);
        Assert.Contains("- [x] 1.1", Read(b, Tasks));
        await Sync(a);
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A sync interrupted after the storage was updated but before the local files were written converges on the next sync")]
    public async Task InterruptedSyncConverges()
    {
        SpyStorage spyB = NewSpy();
        (Computer a, Computer b) = await TwoSyncedComputers(storageB: spyB);

        Tick(5);
        Write(a, Proposal, SeedProposal + "\nAnn's paragraph.\n");
        Write(a, Log, SeedLog + "2026-11-12T08:05:00 | Ann | log | from A\n");
        await Sync(a);
        Tick(5);
        string bLog = SeedLog + "2026-11-12T08:07:00 | Bob | log | from B\n";
        Write(b, Log, bLog);
        Write(b, Tasks, SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1"));

        int puts = spyB.ManifestPuts;
        await Sync(b, hooks: new SyncHooks { SkipLocalApply = true });    // the manifest is replaced, then "the app dies" before any local write

        Assert.Equal(puts + 1, spyB.ManifestPuts);
        Assert.Equal(bLog, Read(b, Log));
        Assert.Equal(SeedProposal, Read(b, Proposal));

        SyncReport next = await Sync(b);
        await Sync(a);

        Assert.Equal(SyncOutcome.Synced, next.Outcome);
        Assert.Equal(Tree(a), Tree(b));
        Assert.Contains("Ann's paragraph.", Read(b, Proposal));
        Assert.Contains("- [x] 1.1", Read(a, Tasks));
        string log = Read(b, Log);
        Assert.Equal(1, log.Split("from A").Length - 1);
        Assert.Equal(1, log.Split("from B").Length - 1);
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(a)).Outcome);
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(b)).Outcome);
    }

    [Fact(DisplayName = "Every path in the manifest keeps the hashes of its earlier versions, newest first")]
    public async Task ManifestKeepsHistory()
    {
        Computer a = NewComputer("A");
        SeedBoard(a);
        await Sync(a);
        for (int i = 1; i <= 3; i++)
        {
            Tick(1);
            Write(a, Tasks, SeedTasks + $"version {i}\n");
            await Sync(a);
        }

        JsonElement entry = ManifestEntries(a.Storage).GetProperty(Tasks);

        Assert.Equal(Sha(SeedTasks + "version 3\n"), entry.GetProperty("hash").GetString());
        Assert.Equal([Sha(SeedTasks + "version 2\n"), Sha(SeedTasks + "version 1\n"), Sha(SeedTasks)], entry.GetProperty("history").EnumerateArray().Select(h => h.GetString()));
        Assert.Empty(ManifestEntries(a.Storage).GetProperty(Yaml).GetProperty("history").EnumerateArray());

        for (int i = 4; i <= 25; i++)
        {
            Tick(1);
            Write(a, Tasks, SeedTasks + $"version {i}\n");
            await Sync(a);
        }

        string?[] history = [.. ManifestEntries(a.Storage).GetProperty(Tasks).GetProperty("history").EnumerateArray().Select(h => h.GetString())];
        Assert.Equal(3, history.Length);        // written with three earlier versions; a manifest with up to twenty is still read
        Assert.Equal(Sha(SeedTasks + "version 24\n"), history[0]);
    }

    [Fact(DisplayName = "The event logs of both computers end up as one log on both")]
    public async Task EventLogsBecomeOne()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        const string shared = "2026-11-12T08:06:00 | Sys | log | shared";

        Tick(15);
        Write(a, Log, SeedLog + "2026-11-12T08:05:00 | Ann | log | a1\n" + shared + "\n2026-11-12T08:09:00 | Ann | log | a2\n");
        Write(b, Log, SeedLog + shared + "\n2026-11-12T08:07:00 | Bob | log | b1\n2026-11-12T08:11:00 | Bob | log | b2\n");
        await Sync(a);
        SyncReport atB = await Sync(b);
        await Sync(a);

        string expected = SeedLog + "2026-11-12T08:05:00 | Ann | log | a1\n" + shared + "\n2026-11-12T08:07:00 | Bob | log | b1\n" +
                          "2026-11-12T08:09:00 | Ann | log | a2\n2026-11-12T08:11:00 | Bob | log | b2\n";
        Assert.Equal([Log], atB.Merged);
        Assert.Empty(atB.Conflicts);
        Assert.Equal(expected, Read(a, Log));
        Assert.Equal(expected, Read(b, Log));
    }

    [Fact(DisplayName = "A file that is not text is not merged: the newer side wins whole and one conflict is reported")]
    public async Task NonTextFileIsNotMerged()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        string picture = Dir + "/diagram.png";
        void WriteBytes(Computer c, params byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(c.Root, picture), bytes);
            File.SetLastWriteTimeUtc(Path.Combine(c.Root, picture), _now);
        }
        WriteBytes(a, 0xC3, 0x28, 0x01);                                  // not valid UTF-8
        await Sync(a);
        await Sync(b);

        Tick(5);
        WriteBytes(a, 0xC3, 0x28, 0x02);
        Tick(5);
        WriteBytes(b, 0xC3, 0x28, 0x03);                                  // the later edit
        await Sync(a);
        SyncReport atB = await Sync(b);

        SyncConflict conflict = Assert.Single(atB.Conflicts);
        Assert.Equal(picture, conflict.Path);
        Assert.Contains("not text", conflict.What);
        Assert.Equal([0xC3, 0x28, 0x03], File.ReadAllBytes(Path.Combine(b.Root, picture)));
        await Sync(a);
        Assert.Equal([0xC3, 0x28, 0x03], File.ReadAllBytes(Path.Combine(a.Root, picture)));
        Assert.False(conflict.DroppedHere);                               // the newer version was this computer's: the storage's was dropped

        Tick(5);
        WriteBytes(a, 0xC3, 0x28, 0x04);
        Tick(5);
        WriteBytes(b, 0xC3, 0x28, 0x05);
        await Sync(b);
        SyncConflict atA = Assert.Single((await Sync(a)).Conflicts);      // the later one is already in the storage: this computer's was dropped
        Assert.True(atA.DroppedHere);
    }

    [Fact(DisplayName = "A manifest that names a path outside the board folder fails the sync and writes nothing")]
    public async Task ManifestPathsStayInsideTheFolder()
    {
        Computer a = NewComputer("A");
        await BoardSyncApi.FolderStorage(StorageFolder).Put("objects/ab/abcdef", Encoding.UTF8.GetBytes("x"), PutCondition.OnlyIfAbsent);
        string manifest = "{\"version\":1,\"entries\":{\"../escaped.txt\":{\"hash\":\"abcdef\",\"clock\":\"000000000000001-0000-X\",\"deleted\":false,\"history\":[]}}}";
        await BoardSyncApi.FolderStorage(StorageFolder).Put("manifest.json", Encoding.UTF8.GetBytes(manifest), PutCondition.Always);

        SyncReport report = await Sync(a);

        Assert.Equal(SyncOutcome.Failed, report.Outcome);
        Assert.Contains("../escaped.txt", report.Message);
        Assert.False(File.Exists(Path.Combine(_temp.FullName, "A", "escaped.txt")));
        Assert.False(Directory.Exists(a.State));
    }
}

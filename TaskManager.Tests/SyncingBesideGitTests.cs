using System.Security.Cryptography;
using System.Text;
using BoardSync;
using BoardSync.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// The sync next to git: the same files are committed, pulled and checked out, so a file that changed under the app's feet is either old content a checkout
/// brought back (the storage wins) or a real edit; and while git is in the middle of an operation, or a board file holds conflict markers, the sync does nothing.
/// Git is simulated by rewriting files directly and by passing the set of paths "the app" wrote.
/// </summary>
public sealed class SyncingBesideGitTests : IDisposable
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

    private static readonly string TasksV2 = SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1");
    private static readonly string TasksV3 = TasksV2.Replace("- [ ] 1.2", "- [x] 1.2");

    private const string MergeMessage = "Sync is paused while a git merge is in progress. Finish or abort it, then sync again.";

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-sync-git-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private void Tick(int minutes) => _now = _now.AddMinutes(minutes);

    // ---- a computer ----------------------------------------------------------------------------------------------------------------------

    private sealed record Computer(string Id, string Root, string State, IBoardStorage Storage);

    private static readonly IReadOnlySet<string> NoAppEdits = new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlySet<string> AppEdits(params string[] paths) => new HashSet<string>(paths, StringComparer.Ordinal);

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");
    private CountingStorage NewSpy() => new(BoardSyncApi.FolderStorage(StorageFolder));

    private Computer NewComputer(string id, IBoardStorage? storage = null) =>
        new(id, Path.Combine(_temp.FullName, id, "changes"), Path.Combine(_temp.FullName, id, "state"), storage ?? BoardSyncApi.FolderStorage(StorageFolder));

    private Task<SyncReport> Sync(Computer c, IReadOnlySet<string>? edited = null, string? gitFolder = null) =>
        BoardSyncApi.Sync(new SyncOptions(c.Root, c.State, c.Storage, c.Id, () => _now, EditedInApp: edited, GitFolder: gitFolder));

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
    private async Task<(Computer A, Computer B)> TwoSyncedComputers(IBoardStorage? storageB = null)
    {
        Computer a = NewComputer("A"), b = NewComputer("B", storageB);
        SeedBoard(a);
        await Sync(a);
        await Sync(b);
        return (a, b);
    }

    /// <summary>A edits the tasks to <paramref name="text"/> and syncs; B follows, so the storage's tasks entry has older versions in its history and B's base is the new one.</summary>
    private async Task<(Computer A, Computer B)> TwoComputersAfterOneEdit(string text, IBoardStorage? storageB = null)
    {
        (Computer a, Computer b) = await TwoSyncedComputers(storageB);
        Tick(5);
        Write(a, Tasks, text);
        await Sync(a);
        await Sync(b);
        Tick(5);
        return (a, b);
    }

    private static SortedDictionary<string, string> Tree(Computer c) =>
        new(Directory.Exists(c.Root)
            ? Directory.EnumerateFiles(c.Root, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(c.Root, f).Replace('\\', '/'), File.ReadAllText)
            : []);

    /// <summary>Every file under the folders with its size and last-write time: equal snapshots mean nothing was written.</summary>
    private static List<string> Snapshot(params string[] folders) =>
        [.. folders.Where(Directory.Exists).SelectMany(f => Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => $"{f}|{new FileInfo(f).Length}|{File.GetLastWriteTimeUtc(f).Ticks}")];

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal);

    private static string ManifestHash(IBoardStorage storage, string path) =>
        System.Text.Json.JsonDocument.Parse(storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes).RootElement
            .GetProperty("entries").GetProperty(path).GetProperty("hash").GetString()!;

    private static bool ManifestDeleted(IBoardStorage storage, string path) =>
        System.Text.Json.JsonDocument.Parse(storage.Get("manifest.json").GetAwaiter().GetResult()!.Bytes).RootElement
            .GetProperty("entries").GetProperty(path).GetProperty("deleted").GetBoolean();

    // ---- git, simulated ------------------------------------------------------------------------------------------------------------------

    private string NewGitFolder(string name = "repo")
    {
        string git = Path.Combine(_temp.FullName, name, ".git");
        Directory.CreateDirectory(git);
        File.WriteAllText(Path.Combine(git, "HEAD"), "ref: refs/heads/main\n");
        return git;
    }

    private const string ConflictedTasks =
        "# Tasks\n\n## 1. Tasks\n\n<<<<<<< HEAD\n- [x] 1.1 write the test\n=======\n- [ ] 1.1 write a test\n>>>>>>> origin/main\n- [ ] 1.2 write the code\n";

    // ---- a storage that counts every call ----------------------------------------------------------------------------------------------

    private sealed class CountingStorage(IBoardStorage inner) : IBoardStorage
    {
        public int Calls { get; private set; }
        public int Writes { get; private set; }

        public bool ConditionalWrites => inner.ConditionalWrites;

        public Task<StoredFile?> Get(string path, CancellationToken ct = default) { Calls++; return inner.Get(path, ct); }
        public Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default) { Calls++; return inner.GetIfChanged(path, version, ct); }
        public Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default) { Calls++; Writes++; return inner.Put(path, bytes, condition, ct); }
        public Task Delete(string path, CancellationToken ct = default) { Calls++; Writes++; return inner.Delete(path, ct); }
        public Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default) { Calls++; return inner.List(prefix, ct); }
    }

    // ---- stale content -------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Old content that a checkout brings back is not uploaded: the storage's newer version returns")]
    public async Task OldContentFromACheckoutIsReplaced()
    {
        CountingStorage spy = NewSpy();
        (Computer a, Computer b) = await TwoComputersAfterOneEdit(TasksV2, spy);

        Write(b, Tasks, SeedTasks);                                   // git checkout of an older commit
        int writesBefore = spy.Writes;
        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal([Tasks], report.Stale);
        Assert.Equal([Tasks], report.Downloaded);
        Assert.Empty(report.Uploaded);
        Assert.Empty(report.Merged);
        Assert.Empty(report.Conflicts);
        Assert.Equal(TasksV2, Read(b, Tasks));
        Assert.Equal(writesBefore, spy.Writes);
        Assert.Equal(Sha(TasksV2), ManifestHash(b.Storage, Tasks));
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(a, NoAppEdits)).Outcome);
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(b, NoAppEdits)).Outcome);
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A file changed outside the app to content the storage has never had is a real edit and is uploaded")]
    public async Task NewContentFromOutsideIsAnEdit()
    {
        (Computer a, Computer b) = await TwoComputersAfterOneEdit(TasksV2);

        Write(b, Tasks, TasksV3);                                     // git pull brought in a commit the storage has not seen
        SyncReport report = await Sync(b, NoAppEdits);
        SyncReport back = await Sync(a, NoAppEdits);

        Assert.Equal([Tasks], report.Uploaded);
        Assert.Empty(report.Stale);
        Assert.Empty(report.Downloaded);
        Assert.Equal(Sha(TasksV3), ManifestHash(b.Storage, Tasks));
        Assert.Equal([Tasks], back.Downloaded);
        Assert.Equal(TasksV3, Read(a, Tasks));
    }

    [Fact(DisplayName = "A file changed outside the app while the storage changed too is merged, not treated as stale")]
    public async Task OutsideChangeWithStorageChangeIsMerged()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Yaml, SeedYaml.Replace("status: Planned", "status: In Progress"));
        await Sync(a);
        Tick(5);
        Write(b, Yaml, SeedYaml.Replace("priority: low", "priority: high"));     // a pull brought in a commit that raised the priority
        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal([Yaml], report.Merged);
        Assert.Empty(report.Stale);
        Assert.Contains("status: In Progress", Read(b, Yaml));
        Assert.Contains("priority: high", Read(b, Yaml));
    }

    [Fact(DisplayName = "A change made in the app is an edit even when it restores old text")]
    public async Task AppEditThatRestoresOldTextIsAnEdit()
    {
        (Computer a, Computer b) = await TwoComputersAfterOneEdit(TasksV2);

        Write(b, Tasks, SeedTasks);                                   // the user unticked the box in the app
        SyncReport report = await Sync(b, AppEdits(Tasks));
        SyncReport back = await Sync(a, NoAppEdits);

        Assert.Equal([Tasks], report.Uploaded);
        Assert.Empty(report.Stale);
        Assert.Equal(Sha(SeedTasks), ManifestHash(b.Storage, Tasks));
        Assert.Equal(SeedTasks, Read(b, Tasks));
        Assert.Equal([Tasks], back.Downloaded);
        Assert.Equal(SeedTasks, Read(a, Tasks));
    }

    [Fact(DisplayName = "Without the list of app edits every local change is an edit, as before")]
    public async Task NoListMeansEveryChangeIsAnEdit()
    {
        (Computer a, Computer b) = await TwoComputersAfterOneEdit(TasksV2);

        Write(b, Tasks, SeedTasks);
        Delete(b, Note);
        SyncReport report = await Sync(b, edited: null);

        Assert.Equal([Tasks], report.Uploaded);
        Assert.Equal([Note], report.DeletedInStorage);
        Assert.Empty(report.Stale);
        Assert.Equal(Sha(SeedTasks), ManifestHash(b.Storage, Tasks));
        Assert.True(ManifestDeleted(b.Storage, Note));
        Assert.Equal(Sha(SeedTasks), Sha(Read(b, Tasks)));
        Assert.Equal([Tasks], (await Sync(a)).Downloaded);
    }

    [Fact(DisplayName = "A file a checkout removed comes back when the storage has moved on, and stays deleted when nobody else changed it")]
    public async Task RemovedByCheckout()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Proposal, SeedProposal + "\nAnn added a paragraph.\n");
        await Sync(a);                                                // the storage moved on for the proposal; B has not seen it
        Tick(5);
        Delete(b, Proposal);                                          // git checkout removed both files
        Delete(b, Tasks);
        SyncReport report = await Sync(b, NoAppEdits);
        SyncReport back = await Sync(a, NoAppEdits);

        Assert.Equal([Proposal], report.Downloaded);
        Assert.Equal([Proposal], report.Stale);
        Assert.Equal(SeedProposal + "\nAnn added a paragraph.\n", Read(b, Proposal));
        Assert.Equal([Tasks], report.DeletedInStorage);
        Assert.False(Exists(b, Tasks));
        Assert.True(ManifestDeleted(b.Storage, Tasks));
        Assert.False(ManifestDeleted(b.Storage, Proposal));
        Assert.Empty(report.Conflicts);
        Assert.Equal([Tasks], back.DeletedHere);
        Assert.Equal(Tree(a), Tree(b));
    }

    [Fact(DisplayName = "A file the app deleted is deleted in the storage even when the storage has moved on, as before")]
    public async Task AppDeletionFollowsTheOldRules()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Proposal, SeedProposal + "\nAnn added a paragraph.\n");
        await Sync(a);
        Tick(5);
        Delete(b, Proposal);
        SyncReport report = await Sync(b, AppEdits(Proposal));

        Assert.Equal([Proposal], report.DeletedInStorage);
        Assert.Empty(report.Stale);
        Assert.Equal(Proposal, Assert.Single(report.Conflicts).Path);
        Assert.False(Exists(b, Proposal));
    }

    [Fact(DisplayName = "A file a checkout brings back after it was deleted in the storage is deleted again")]
    public async Task ResurrectedFileIsDeletedAgain()
    {
        CountingStorage spy = NewSpy();
        (Computer a, Computer b) = await TwoSyncedComputers(spy);

        Tick(5);
        Delete(a, Note);
        await Sync(a);
        await Sync(b);                                                // the note is gone on both computers
        Tick(5);
        Write(b, Note, SeedNote);                                     // git checkout brings the old commit's file back
        int writesBefore = spy.Writes;
        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal([Note], report.DeletedHere);
        Assert.Equal([Note], report.Stale);
        Assert.Empty(report.Uploaded);
        Assert.False(Exists(b, Note));
        Assert.Equal(writesBefore, spy.Writes);
        Assert.True(ManifestDeleted(b.Storage, Note));
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(b, NoAppEdits)).Outcome);
    }

    [Fact(DisplayName = "An earlier version of a file deleted in the storage that comes back through git is deleted again")]
    public async Task EarlierVersionOfDeletedFileIsDeletedAgain()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Tasks, TasksV2);
        await Sync(a);
        Tick(5);
        Delete(a, Tasks);
        await Sync(a);
        await Sync(b);
        Assert.False(Exists(b, Tasks));
        Tick(5);
        Write(b, Tasks, SeedTasks);                                   // the first version, now in the history of the tombstone
        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal([Tasks], report.DeletedHere);
        Assert.Equal([Tasks], report.Stale);
        Assert.False(Exists(b, Tasks));
    }

    [Fact(DisplayName = "A file the app recreates after it was deleted in the storage is an edit and comes back")]
    public async Task AppRecreatedFileIsAnEdit()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Delete(a, Note);
        await Sync(a);
        await Sync(b);
        Tick(5);
        Write(b, Note, SeedNote);                                     // the user wrote the same note again in the app
        SyncReport report = await Sync(b, AppEdits(Note));

        Assert.Equal([Note], report.Uploaded);
        Assert.Empty(report.Stale);
        Assert.False(ManifestDeleted(b.Storage, Note));
        Assert.Equal([Note], (await Sync(a)).Downloaded);
    }

    [Fact(DisplayName = "The sync report lists the paths where stale content was replaced")]
    public async Task ReportListsStalePaths()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Tasks, TasksV2);
        Write(a, Proposal, SeedProposal + "\nAnn added a paragraph.\n");
        Delete(a, Note);
        await Sync(a);
        await Sync(b);
        Tick(5);
        Write(b, Tasks, SeedTasks);                                   // old content back
        Write(b, Proposal, SeedProposal);                             // old content back
        Write(b, Note, SeedNote);                                     // a deleted file back
        Write(b, Log, SeedLog + "2026-11-12T08:10:00 | Bob | log | a real edit\n");
        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal(Sorted([Note, Proposal, Tasks]), Sorted(report.Stale));
        Assert.Equal(Sorted([Proposal, Tasks]), Sorted(report.Downloaded));
        Assert.Equal([Note], report.DeletedHere);
        Assert.Equal([Log], report.Uploaded);
        Assert.Empty((await Sync(b, NoAppEdits)).Stale);
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(b, NoAppEdits)).Outcome);
        Assert.Empty(new SyncReport(SyncOutcome.NothingToDo, null, [], [], [], [], [], [], []).Stale);    // existing callers that build a report still compile
    }

    [Fact(DisplayName = "On a first connection nothing is stale: the computer's files are backed up and replaced as before")]
    public async Task FirstConnectionHasNothingStale()
    {
        (Computer a, _) = await TwoSyncedComputers();
        Tick(5);
        Write(a, Tasks, TasksV2);
        await Sync(a);
        Tick(5);

        Computer c = NewComputer("C");
        Write(c, Tasks, SeedTasks);                                   // a clone with an older commit checked out
        SyncReport report = await Sync(c, NoAppEdits);

        Assert.Empty(report.Stale);
        Assert.Single(report.BackedUp);
        Assert.Equal(TasksV2, Read(c, Tasks));
    }

    // ---- git in the middle of something --------------------------------------------------------------------------------------------------

    private async Task<(Computer A, Computer B, CountingStorage Spy)> BoardWithPendingEdit()
    {
        CountingStorage spy = NewSpy();
        (Computer a, Computer b) = await TwoSyncedComputers(spy);
        Tick(5);
        Write(b, Tasks, TasksV2);
        return (a, b, spy);
    }

    [Fact(DisplayName = "Sync is paused while a git merge, rebase, cherry-pick or revert is in progress, and writes nothing")]
    public async Task PausedDuringAGitOperation()
    {
        (Computer a, Computer b, CountingStorage spy) = await BoardWithPendingEdit();
        string git = NewGitFolder();

        (string Marker, bool IsDir, string Name)[] operations =
        [
            ("MERGE_HEAD", false, "merge"),
            ("rebase-merge", true, "rebase"),
            ("rebase-apply", true, "rebase"),
            ("CHERRY_PICK_HEAD", false, "cherry-pick"),
            ("REVERT_HEAD", false, "revert"),
        ];
        foreach ((string marker, bool isDir, string name) in operations)
        {
            string path = Path.Combine(git, marker);
            if (isDir) Directory.CreateDirectory(path); else File.WriteAllText(path, "0123456789abcdef0123456789abcdef01234567\n");

            List<string> before = Snapshot(b.Root, b.State, StorageFolder);
            int calls = spy.Calls;
            SyncReport report = await Sync(b, AppEdits(Tasks), git);

            Assert.Equal(SyncOutcome.Blocked, report.Outcome);
            Assert.Equal($"Sync is paused while a git {name} is in progress. Finish or abort it, then sync again.", report.Message);
            Assert.Empty(report.Uploaded);
            Assert.Equal(calls, spy.Calls);                           // no storage call at all
            Assert.Equal(before, Snapshot(b.Root, b.State, StorageFolder));
            Assert.Equal(MergeMessage.Replace("merge", name), BoardSyncApi.GitPause(b.Root, git));

            if (isDir) Directory.Delete(path); else File.Delete(path);
        }
        Assert.Equal(SeedTasks, Read(a, Tasks));
    }

    [Fact(DisplayName = "A blocked sync says whether git is busy or the deletion brake stopped it")]
    public async Task ABlockedSyncSaysWhy()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        SeedBoard(a);
        for (int i = 2; i <= 13; i++) Write(a, $"big-change/notes/n{i}.md", $"# Note {i}\n\nText {i}.\n");   // 17 files in all
        Assert.Equal(SyncBlock.None, (await Sync(a)).Blocked);
        Assert.Equal(SyncBlock.None, (await Sync(b)).Blocked);
        Assert.Equal(SyncBlock.None, (await Sync(b)).Blocked);                                            // NothingToDo is not blocked either

        Tick(5);
        string git = NewGitFolder();
        string mergeHead = Path.Combine(git, "MERGE_HEAD");
        File.WriteAllText(mergeHead, "0123456789abcdef0123456789abcdef01234567\n");
        SyncReport busy = await Sync(a, AppEdits(), git);
        Assert.Equal(SyncOutcome.Blocked, busy.Outcome);
        Assert.Equal(SyncBlock.GitBusy, busy.Blocked);
        Assert.Contains("git merge", busy.Message);
        File.Delete(mergeHead);

        for (int i = 2; i <= 12; i++) Delete(a, $"big-change/notes/n{i}.md");                              // 11 of 17: more than the brake lets through
        SyncReport brake = await Sync(a, AppEdits(), git);
        Assert.Equal(SyncOutcome.Blocked, brake.Outcome);
        Assert.Equal(SyncBlock.MassDelete, brake.Blocked);
        Assert.Contains("allow it", brake.Message);

        File.WriteAllText(mergeHead, "0123456789abcdef0123456789abcdef01234567\n");                        // both at once: git comes first, nothing is read
        Assert.Equal(SyncBlock.GitBusy, (await Sync(a, AppEdits(), git)).Blocked);
        File.Delete(mergeHead);

        SyncReport allowed = await BoardSyncApi.Sync(new SyncOptions(a.Root, a.State, a.Storage, a.Id, () => _now, AllowMassDelete: true, EditedInApp: AppEdits(), GitFolder: git));
        Assert.Equal(SyncOutcome.Synced, allowed.Outcome);
        Assert.Equal(SyncBlock.None, allowed.Blocked);

        File.WriteAllText(Path.Combine(StorageFolder, "manifest.json"), "not a manifest");
        SyncReport failed = await Sync(NewComputer("C"));
        Assert.Equal(SyncOutcome.Failed, failed.Outcome);
        Assert.Equal(SyncBlock.None, failed.Blocked);
        Assert.Equal(SyncBlock.None, new SyncReport(SyncOutcome.Synced, null, [], [], [], [], [], [], []).Blocked);   // existing constructions keep compiling
    }

    [Fact(DisplayName = "Sync is paused while a board file still has merge conflict markers, and names the file")]
    public async Task PausedWithConflictMarkers()
    {
        (_, Computer b, CountingStorage spy) = await BoardWithPendingEdit();
        string git = NewGitFolder();
        Write(b, Tasks, ConflictedTasks);

        List<string> before = Snapshot(b.Root, b.State, StorageFolder);
        int calls = spy.Calls;
        SyncReport one = await Sync(b, AppEdits(Tasks), git);

        Assert.Equal(SyncOutcome.Blocked, one.Outcome);
        Assert.Equal($"Sync is paused: {Tasks} still has merge conflict markers.", one.Message);
        Assert.Equal(calls, spy.Calls);
        Assert.Equal(before, Snapshot(b.Root, b.State, StorageFolder));

        Write(b, Proposal, "# Add health check\n\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> origin/main\n");
        Write(b, Note, "<<<<<<< a\nx\n=======\ny\n>>>>>>> b\n");
        SyncReport several = await Sync(b, AppEdits(Tasks), git);

        Assert.Equal(SyncOutcome.Blocked, several.Outcome);
        Assert.Equal($"Sync is paused: {Note} and 2 more still have merge conflict markers.", several.Message);
        Assert.Equal(several.Message, BoardSyncApi.GitPause(b.Root, git));
    }

    [Fact(DisplayName = "Lines that only look like conflict markers do not pause the sync")]
    public async Task LookalikesDoNotPause()
    {
        (_, Computer b, _) = await BoardWithPendingEdit();
        string git = NewGitFolder();
        Write(b, Proposal, "# Add health check\n\nA heading\n=======\n\nA table\n\n<<<<<<<not a marker\n>>>>>>> before the opener\n");
        Write(b, Note, ">>>>>>> x\ntext\n=======\n<<<<<<< y\n");               // right lines, wrong order

        Assert.Null(BoardSyncApi.GitPause(b.Root, git));
        Assert.Equal(SyncOutcome.Synced, (await Sync(b, AppEdits(Tasks, Proposal, Note), git)).Outcome);
    }

    [Fact(DisplayName = "A git folder given as a worktree's .git file is resolved, and a missing git folder means no check")]
    public async Task WorktreeGitFileAndMissingFolder()
    {
        (_, Computer b, CountingStorage spy) = await BoardWithPendingEdit();

        // A worktree: <repo>/.git/worktrees/wt holds the state, <wt>/.git is a file pointing at it (absolute, then relative).
        string gitDir = Path.Combine(_temp.FullName, "repo", ".git", "worktrees", "wt");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "MERGE_HEAD"), "0123456789abcdef0123456789abcdef01234567\n");
        string worktree = Path.Combine(_temp.FullName, "wt");
        Directory.CreateDirectory(worktree);
        string gitFile = Path.Combine(worktree, ".git");

        File.WriteAllText(gitFile, $"gitdir: {gitDir}\n");
        Assert.Equal(MergeMessage, BoardSyncApi.GitPause(b.Root, gitFile));
        SyncReport absolute = await Sync(b, AppEdits(Tasks), gitFile);
        Assert.Equal(SyncOutcome.Blocked, absolute.Outcome);
        Assert.Equal(MergeMessage, absolute.Message);

        File.WriteAllText(gitFile, "gitdir: ../repo/.git/worktrees/wt\n");
        Assert.Equal(MergeMessage, BoardSyncApi.GitPause(b.Root, gitFile));

        Assert.Equal(MergeMessage, BoardSyncApi.GitPause(b.Root, gitDir));    // the directory itself works too

        string missing = Path.Combine(_temp.FullName, "nowhere", ".git");
        Assert.Null(BoardSyncApi.GitPause(b.Root, missing));
        Assert.Null(BoardSyncApi.GitPause(b.Root, null));
        File.WriteAllText(gitFile, $"gitdir: {Path.Combine(_temp.FullName, "gone")}\n");
        Assert.Null(BoardSyncApi.GitPause(b.Root, gitFile));                  // points at a folder that does not exist

        int calls = spy.Calls;
        SyncReport report = await Sync(b, AppEdits(Tasks), missing);
        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal([Tasks], report.Uploaded);
        Assert.True(spy.Calls > calls);
    }

    [Fact(DisplayName = "After the merge is finished the next sync runs normally")]
    public async Task ResumesAfterTheMerge()
    {
        (Computer a, Computer b, _) = await BoardWithPendingEdit();
        string git = NewGitFolder();
        string mergeHead = Path.Combine(git, "MERGE_HEAD");
        File.WriteAllText(mergeHead, "0123456789abcdef0123456789abcdef01234567\n");
        Write(b, Log, ConflictedTasks);

        Assert.Equal(SyncOutcome.Blocked, (await Sync(b, AppEdits(Tasks, Log), git)).Outcome);

        File.Delete(mergeHead);
        Assert.Equal(SyncOutcome.Blocked, (await Sync(b, AppEdits(Tasks, Log), git)).Outcome);    // the markers are still there

        Write(b, Log, SeedLog + "2026-11-12T08:10:00 | Bob | log | resolved\n");
        SyncReport report = await Sync(b, AppEdits(Tasks, Log), git);
        SyncReport back = await Sync(a, NoAppEdits, git);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal(Sorted([Log, Tasks]), Sorted(report.Uploaded));
        Assert.Equal(Sorted([Log, Tasks]), Sorted(back.Downloaded));
        Assert.Equal(Tree(a), Tree(b));
        Assert.Null(BoardSyncApi.GitPause(b.Root, git));
    }
}

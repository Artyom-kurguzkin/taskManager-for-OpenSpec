using System.Text;
using BoardSync;
using BoardSync.PublicDTOs;

namespace TaskManager.Tests;

/// <summary>
/// The sync never erases a file on this computer: what it removes, and what it overwrites when nothing else holds the bytes, is moved to
/// StateFolder/trash/&lt;yyyyMMdd-HHmmss of the sync&gt;/&lt;path&gt; first, and trash older than thirty days is pruned.
/// Two computers (two directories, two state folders), one shared storage (a temp folder), a clock the test advances by hand.
/// </summary>
public sealed class KeepingWhatSyncDeletesTests : IDisposable
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
    private static readonly string[] SeedTexts = [SeedYaml, SeedProposal, SeedTasks, SeedLog, SeedNote];

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-sync-trash-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private void Tick(int minutes) => _now = _now.AddMinutes(minutes);

    // ---- a computer ----------------------------------------------------------------------------------------------------------------------

    private sealed record Computer(string Id, string Root, string State, IBoardStorage Storage);

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");

    private Computer NewComputer(string id) =>
        new(id, Path.Combine(_temp.FullName, id, "changes"), Path.Combine(_temp.FullName, id, "state"), BoardSyncApi.FolderStorage(StorageFolder));

    private Task<SyncReport> Sync(Computer c, IReadOnlySet<string>? edited = null, string? gitFolder = null) =>
        BoardSyncApi.Sync(new SyncOptions(c.Root, c.State, c.Storage, c.Id, () => _now, EditedInApp: edited, GitFolder: gitFolder));

    private void Write(Computer c, string path, string text) => WriteBytes(c, path, new UTF8Encoding(false).GetBytes(text));

    private void WriteBytes(Computer c, string path, params byte[] bytes)
    {
        string file = Path.Combine(c.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
        File.SetLastWriteTimeUtc(file, _now);
    }

    private static bool Exists(Computer c, string path) => File.Exists(Path.Combine(c.Root, path));
    private static void Delete(Computer c, string path) => File.Delete(Path.Combine(c.Root, path));
    private static string TrashRoot(Computer c) => Path.Combine(c.State, "trash");

    private void SeedBoard(Computer c)
    {
        for (int i = 0; i < Seeded.Length; i++) Write(c, Seeded[i], SeedTexts[i]);
    }

    /// <summary>Both computers hold the seeded board and have synced it.</summary>
    private async Task<(Computer A, Computer B)> TwoSyncedComputers()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        SeedBoard(a);
        await Sync(a);
        await Sync(b);
        return (a, b);
    }

    private string Stamp => _now.ToString("yyyyMMdd-HHmmss");

    /// <summary>Where the trash copy of <paramref name="path"/> made by a sync running now should be.</summary>
    private string TrashOf(Computer c, string path) => Path.Combine(TrashRoot(c), Stamp, path.Replace('/', Path.DirectorySeparatorChar));

    private static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal);

    private static IReadOnlySet<string> NoAppEdits => new HashSet<string>(StringComparer.Ordinal);

    // ---- the facts -----------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "A file deleted on the other computer is moved to this computer's trash, not erased")]
    public async Task DeletedFileGoesToTheTrash()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(10);
        Delete(a, Note);
        await Sync(a);
        SyncReport atB = await Sync(b);

        Assert.Equal([Note], atB.DeletedHere);
        Assert.Equal([TrashOf(b, Note)], atB.Trashed);
        Assert.False(Exists(b, Note));
        Assert.False(Directory.Exists(Path.Combine(b.Root, Dir, "notes")));    // the emptied folder goes as before
        Assert.True(Directory.Exists(b.Root));
        Assert.Equal(SeedNote, File.ReadAllText(TrashOf(b, Note)));
        Assert.Empty((await Sync(a)).Trashed);                                 // the computer that deleted it had nothing to remove
    }

    [Fact(DisplayName = "An edit that loses to a delete is in the trash with the edited text, and the sync report lists it")]
    public async Task LosingEditIsInTheTrash()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(10);
        string edited = SeedProposal + "\nAn edit that will lose.\n";
        Write(b, Proposal, edited);
        Tick(10);
        Delete(a, Proposal);
        await Sync(a);
        SyncReport atB = await Sync(b);

        Assert.Equal([Proposal], atB.DeletedHere);
        Assert.Equal(Proposal, Assert.Single(atB.Conflicts).Path);
        Assert.Equal([TrashOf(b, Proposal)], atB.Trashed);
        Assert.Equal(edited, File.ReadAllText(TrashOf(b, Proposal)));
        Assert.False(Exists(b, Proposal));
    }

    [Fact(DisplayName = "Archiving a work item on one computer moves its folder on the other and the old files are in the trash")]
    public async Task ArchivedFolderMovesAndOldFilesAreInTheTrash()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        const string Archive = "archive/2026-11-12-" + Dir;

        Tick(10);
        foreach (string path in Seeded)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(a.Root, Archive, path[(Dir.Length + 1)..]))!);
            File.Move(Path.Combine(a.Root, path), Path.Combine(a.Root, Archive, path[(Dir.Length + 1)..]));
        }
        Directory.Delete(Path.Combine(a.Root, Dir), true);
        await Sync(a);
        SyncReport atB = await Sync(b);

        Assert.Equal(Sorted(Seeded), Sorted(atB.DeletedHere));
        Assert.Equal(Sorted(Seeded.Select(p => Archive + p[Dir.Length..])), Sorted(atB.Downloaded));
        Assert.Equal(Sorted(Seeded.Select(p => TrashOf(b, p))), Sorted(atB.Trashed));
        for (int i = 0; i < Seeded.Length; i++)
        {
            Assert.Equal(SeedTexts[i], File.ReadAllText(TrashOf(b, Seeded[i])));
            Assert.Equal(SeedTexts[i], File.ReadAllText(Path.Combine(b.Root, Archive + Seeded[i][Dir.Length..])));
        }
        Assert.False(Directory.Exists(Path.Combine(b.Root, Dir)));
    }

    [Fact(DisplayName = "A file that is not text and loses a clash is kept in the trash before it is replaced")]
    public async Task NonTextClashLoserIsTrashed()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        string picture = Dir + "/diagram.png";
        WriteBytes(a, picture, 0xC3, 0x28, 0x01);                         // not valid UTF-8
        await Sync(a);
        await Sync(b);

        Tick(5);
        WriteBytes(b, picture, 0xC3, 0x28, 0x03);                         // the earlier edit, never synced
        Tick(5);
        WriteBytes(a, picture, 0xC3, 0x28, 0x02);                         // the later edit wins whole
        await Sync(a);
        SyncReport atB = await Sync(b);

        Assert.Equal([picture], atB.Downloaded);
        Assert.Contains("not text", Assert.Single(atB.Conflicts).What);
        Assert.Equal([0xC3, 0x28, 0x02], File.ReadAllBytes(Path.Combine(b.Root, picture)));
        Assert.Equal([TrashOf(b, picture)], atB.Trashed);
        Assert.Equal([0xC3, 0x28, 0x03], File.ReadAllBytes(TrashOf(b, picture)));
        Assert.Empty(atB.DeletedHere);
    }

    [Fact(DisplayName = "A file that is not text and wins a clash is not trashed, because nothing of it is lost")]
    public async Task NonTextClashWinnerIsNotTrashed()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        string picture = Dir + "/diagram.png";
        WriteBytes(a, picture, 0xC3, 0x28, 0x01);
        await Sync(a);
        await Sync(b);

        Tick(5);
        WriteBytes(b, picture, 0xC3, 0x28, 0x03);                         // the earlier edit
        Tick(5);
        WriteBytes(a, picture, 0xC3, 0x28, 0x02);                         // the later edit
        await Sync(b);                                                    // the earlier edit is in the storage now, as the older version
        SyncReport atA = await Sync(a);

        Assert.Equal([picture], atA.Uploaded);
        Assert.Contains("not text", Assert.Single(atA.Conflicts).What);
        Assert.Equal([0xC3, 0x28, 0x02], File.ReadAllBytes(Path.Combine(a.Root, picture)));
        Assert.Empty(atA.Trashed);
        Assert.False(Directory.Exists(TrashRoot(a)));
    }

    [Fact(DisplayName = "A file the sync replaces with the storage's newer version is not trashed when its old content is still in the storage")]
    public async Task ReplacedFileWhoseOldContentIsInTheStorageIsNotTrashed()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        string v2 = SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1");

        // Only the storage changed: this computer's file is the last synced content, which the storage keeps.
        Tick(5);
        Write(a, Tasks, v2);
        await Sync(a);
        SyncReport plain = await Sync(b);
        Assert.Equal([Tasks], plain.Downloaded);
        Assert.Empty(plain.Trashed);

        // Stale: a checkout puts the first version back, the storage's newer version returns.
        Tick(5);
        Write(b, Tasks, SeedTasks);
        SyncReport stale = await Sync(b, NoAppEdits);
        Assert.Equal([Tasks], stale.Stale);
        Assert.Equal([Tasks], stale.Downloaded);
        Assert.Equal(v2, File.ReadAllText(Path.Combine(b.Root, Tasks)));
        Assert.Empty(stale.Trashed);
        Assert.Single(stale.Replaced);      // the old local content is still copied into the trash (reported as replaced, not as trashed)
    }

    [Fact(DisplayName = "A file that is merged is not trashed, because the sides it drops are listed in the report's conflicts")]
    public async Task MergedFileIsNotTrashed()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Yaml, SeedYaml.Replace("status: Planned", "status: In Progress"));
        Tick(5);
        Write(b, Yaml, SeedYaml.Replace("status: Planned", "status: In Review"));
        await Sync(a);
        SyncReport atB = await Sync(b);

        Assert.Equal([Yaml], atB.Merged);
        Assert.NotEmpty(atB.Conflicts);
        Assert.Empty(atB.Trashed);
        Assert.False(Directory.Exists(TrashRoot(b)));
    }

    [Fact(DisplayName = "The sync report names the trash folder and the backup folder it used")]
    public async Task ReportNamesTheTrashAndBackupFolders()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        SeedBoard(a);
        SyncReport first = await Sync(a);
        Assert.Null(first.TrashFolder);
        Assert.Null(first.BackupFolder);

        Write(b, Tasks, "my own tasks\n");
        Tick(5);
        SyncReport connect = await Sync(b);                                    // a first connection: a backup, no trash
        Assert.Equal(Path.Combine(b.State, "backup", Stamp), connect.BackupFolder);
        Assert.StartsWith(connect.BackupFolder!, Assert.Single(connect.BackedUp));
        Assert.Null(connect.TrashFolder);

        Tick(10);
        Delete(a, Note);
        await Sync(a);
        SyncReport atB = await Sync(b);                                        // a deletion: trash, no backup
        Assert.Equal(Path.Combine(TrashRoot(b), Stamp), atB.TrashFolder);
        Assert.All(atB.Trashed, f => Assert.StartsWith(atB.TrashFolder!, f));
        Assert.True(Directory.Exists(atB.TrashFolder));
        Assert.Null(atB.BackupFolder);

        Tick(5);
        SyncReport quiet = await Sync(b);
        Assert.Null(quiet.TrashFolder);
        Assert.Null(quiet.BackupFolder);
    }

    [Fact(DisplayName = "On a first connection the replaced files are in the backup folder and not in the trash")]
    public async Task FirstConnectionUsesTheBackupNotTheTrash()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        SeedBoard(a);
        await Sync(a);
        Write(b, Tasks, "my own tasks\n");
        Tick(5);

        SyncReport atB = await Sync(b);

        Assert.Single(atB.BackedUp);
        Assert.Empty(atB.Trashed);
        Assert.False(Directory.Exists(TrashRoot(b)));
    }

    [Fact(DisplayName = "A file that git's checkout made stale and the sync deletes again is moved to the trash too")]
    public async Task StaleDeleteGoesToTheTrash()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Delete(a, Note);
        await Sync(a);
        await Sync(b);
        Tick(5);
        Write(b, Note, SeedNote);                                         // a checkout brings the deleted file back
        SyncReport report = await Sync(b, NoAppEdits);

        Assert.Equal([Note], report.DeletedHere);
        Assert.Equal([Note], report.Stale);
        Assert.Equal([TrashOf(b, Note)], report.Trashed);
        Assert.Equal(SeedNote, File.ReadAllText(TrashOf(b, Note)));
        Assert.False(Exists(b, Note));
    }

    [Fact(DisplayName = "Trash older than thirty days is removed after a sync, and nothing newer is")]
    public async Task OldTrashIsPruned()
    {
        (_, Computer b) = await TwoSyncedComputers();
        string trash = TrashRoot(b);
        string Made(string name, string file = "n1.md")
        {
            string folder = Path.Combine(trash, name);
            Directory.CreateDirectory(Path.Combine(folder, "notes"));
            File.WriteAllText(Path.Combine(folder, "notes", file), "kept for a while");
            return folder;
        }
        // now is 2026-11-12 08:00:00
        string old = Made("20261001-080000"), older = Made("20250101-000000"), edge = Made("20261013-080001"), recent = Made("20261110-120000");
        string stray = Made("not-a-timestamp"), alsoOld = Path.Combine(trash, "20260101-000000.txt");
        File.WriteAllText(alsoOld, "a file, not a trash folder");
        File.WriteAllText(Path.Combine(b.State, "state-note.txt"), "outside the trash");

        SyncReport quiet = await Sync(b);

        Assert.Equal(SyncOutcome.NothingToDo, quiet.Outcome);
        Assert.False(Directory.Exists(old));
        Assert.False(Directory.Exists(older));
        Assert.True(Directory.Exists(edge));                              // 29 days and 23 hours: not yet
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(stray));                             // a name that is not a timestamp is not ours to touch
        Assert.True(File.Exists(alsoOld));
        Assert.True(File.Exists(Path.Combine(b.State, "state-note.txt")));
        Assert.True(Directory.Exists(trash));
        Assert.Equal(Sorted(Seeded), Sorted(Directory.EnumerateFiles(b.Root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(b.Root, f).Replace('\\', '/'))));
    }

    [Fact(DisplayName = "A sync that does not finish does not prune the trash")]
    public async Task BlockedSyncDoesNotPrune()
    {
        (_, Computer b) = await TwoSyncedComputers();
        string git = Path.Combine(_temp.FullName, "git");
        Directory.CreateDirectory(git);
        File.WriteAllText(Path.Combine(git, "MERGE_HEAD"), "abc");
        string old = Path.Combine(TrashRoot(b), "20250101-000000");
        Directory.CreateDirectory(old);

        SyncReport report = await Sync(b, gitFolder: git);

        Assert.Equal(SyncOutcome.Blocked, report.Outcome);
        Assert.True(Directory.Exists(old));
    }

    [Fact(DisplayName = "A file that cannot be moved to the trash is not deleted, and the next sync tries again")]
    public async Task UnmovableFileIsKeptAndRetried()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(10);
        Delete(a, Note);
        Delete(a, Proposal);
        await Sync(a);
        File.WriteAllText(TrashRoot(b), "a file where the trash folder must be");     // no trash folder can be made
        SyncReport blocked = await Sync(b);

        Assert.Empty(blocked.DeletedHere);
        Assert.Empty(blocked.Trashed);
        Assert.Equal(SeedNote, File.ReadAllText(Path.Combine(b.Root, Note)));
        Assert.Equal(SeedProposal, File.ReadAllText(Path.Combine(b.Root, Proposal)));
        Assert.True(File.Exists(TrashRoot(b)));                           // and the blocker itself is untouched

        File.Delete(TrashRoot(b));
        Tick(1);
        SyncReport retry = await Sync(b);

        Assert.Equal(Sorted([Note, Proposal]), Sorted(retry.DeletedHere));
        Assert.Equal(Sorted([Note, Proposal]), Sorted(retry.Trashed.Select(p => Path.GetRelativePath(Path.Combine(TrashRoot(b), Stamp), p).Replace('\\', '/'))));
        Assert.False(Exists(b, Note));
        Assert.False(Exists(b, Proposal));
        Assert.Equal(SeedNote, File.ReadAllText(TrashOf(b, Note)));
    }

    [Fact(DisplayName = "A file that is not text and cannot be moved to the trash is not replaced, and the next sync tries again")]
    public async Task UnmovableNonTextFileIsKeptAndRetried()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();
        string picture = Dir + "/diagram.png";
        WriteBytes(a, picture, 0xC3, 0x28, 0x01);
        await Sync(a);
        await Sync(b);
        Tick(5);
        WriteBytes(b, picture, 0xC3, 0x28, 0x03);
        Tick(5);
        WriteBytes(a, picture, 0xC3, 0x28, 0x02);
        await Sync(a);
        File.WriteAllText(TrashRoot(b), "a file where the trash folder must be");

        SyncReport blocked = await Sync(b);

        Assert.Empty(blocked.Downloaded);
        Assert.Empty(blocked.Trashed);
        Assert.Equal([0xC3, 0x28, 0x03], File.ReadAllBytes(Path.Combine(b.Root, picture)));

        File.Delete(TrashRoot(b));
        Tick(1);
        SyncReport retry = await Sync(b);

        Assert.Equal([TrashOf(b, picture)], retry.Trashed);
        Assert.Equal([0xC3, 0x28, 0x03], File.ReadAllBytes(TrashOf(b, picture)));
        Assert.Equal([0xC3, 0x28, 0x02], File.ReadAllBytes(Path.Combine(b.Root, picture)));
    }

    [Fact(DisplayName = "Two files with the same name trashed in the same second are both kept")]
    public async Task SameSecondSameNameKeepsBoth()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Delete(a, Note);
        await Sync(a);
        SyncReport first = await Sync(b);                                 // the note is in the trash of 08:00:00

        _now = _now.AddMilliseconds(100);
        string second = "---\nid: 1\nby: Ann\ncreated: 2026-11-12T08:00:00\n---\n# Written again\n";
        Write(a, Note, second);
        await Sync(a);
        await Sync(b);
        _now = _now.AddMilliseconds(100);
        Delete(a, Note);
        await Sync(a);
        SyncReport again = await Sync(b);                                 // deleted again, still in the same second

        string firstCopy = Assert.Single(first.Trashed), secondCopy = Assert.Single(again.Trashed);
        Assert.NotEqual(firstCopy, secondCopy);
        Assert.Equal(SeedNote, File.ReadAllText(firstCopy));
        Assert.Equal(second, File.ReadAllText(secondCopy));
        Assert.StartsWith(Path.Combine(TrashRoot(b), Stamp) + Path.DirectorySeparatorChar, secondCopy);
    }

    [Fact(DisplayName = "A sync that deletes nothing creates no trash folder")]
    public async Task NoDeleteNoTrashFolder()
    {
        (Computer a, Computer b) = await TwoSyncedComputers();

        Tick(5);
        Write(a, Tasks, SeedTasks.Replace("- [ ] 1.1", "- [x] 1.1"));
        Write(a, Dir + "/design.md", "# Design\n");
        List<SyncReport> reports = [];
        reports.Add(await Sync(a));
        reports.Add(await Sync(b));
        Tick(5);
        Write(b, Log, SeedLog + "2026-11-12T08:10:00 | Bob | log | more\n");
        reports.Add(await Sync(b));
        reports.Add(await Sync(a));
        reports.Add(await Sync(a));

        // Downloads copy what they replace into the trash (reported as Replaced); nothing was deleted, so nothing is Trashed.
        Assert.All(reports, r => Assert.Empty(r.Trashed));
        Assert.All(reports, r => Assert.Empty(r.DeletedHere));
    }

    [Fact(DisplayName = "A report built without a trash list has none, and the existing report constructions still compile")]
    public void ReportsHaveNoTrashByDefault()
    {
        Assert.Empty(new SyncReport(SyncOutcome.NothingToDo, null, [], [], [], [], [], [], []).Trashed);
        Assert.Empty(new SyncReport(SyncOutcome.NothingToDo, null, [], [], [], [], [], [], [], []).Trashed);
        Assert.Equal(["x"], new SyncReport(SyncOutcome.Synced, null, [], [], [], [], [], [], [], [], ["x"]).Trashed);
    }
}

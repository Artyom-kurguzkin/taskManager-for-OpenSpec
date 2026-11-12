using System.Text;
using BoardSync;
using BoardSync.PublicDTOs;
using BoardSync.Storage;
using BoardSync.Sync;

namespace TaskManager.Tests;

/// <summary>
/// A second sync beside the board's, for the attachments folder (pasted pictures and attached files): the same engine, whole files only, into the same storage under "attachments/".
/// Two computers (two folders, two state folders), one folder storage, a clock the test advances by hand.
/// </summary>
public sealed class SyncingAttachmentsTests : IDisposable
{
    private const string Prefix = "attachments";
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFE, 0x00, 0xC3, 0x28, 0x80, 0x81, 0x00, 0x42];
    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0xE2, 0xE3, 0xCF, 0xD3, 0xFF, 0x00, 0xC0, 0xAF];

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("rtm-att-");
    private DateTime _now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { _temp.Delete(true); } catch (IOException) { } }

    private void Tick(int minutes) => _now = _now.AddMinutes(minutes);

    // ---- a computer ----------------------------------------------------------------------------------------------------------------------

    private sealed record Computer(string Id, string Root, string State, IBoardStorage Storage);

    private string StorageFolder => Path.Combine(_temp.FullName, "storage");

    /// <summary>A computer whose attachments folder syncs through a view under "attachments/" of the shared folder storage.</summary>
    private Computer NewComputer(string id, string? storageFolder = null) =>
        new(id, Path.Combine(_temp.FullName, id, "attachments"), Path.Combine(_temp.FullName, id, "state"),
            BoardSyncApi.Under(BoardSyncApi.FolderStorage(storageFolder ?? StorageFolder), Prefix));

    private Task<SyncReport> Sync(Computer c, bool wholeFiles = true, string? git = null, SyncHooks? hooks = null)
    {
        SyncOptions options = new(c.Root, c.State, c.Storage, c.Id, () => _now, GitFolder: git, WholeFilesOnly: wholeFiles);
        return SyncEngine.Run(options, hooks, default);
    }

    private void Write(Computer c, string path, byte[] bytes, DateTime? modified = null)
    {
        string file = Path.Combine(c.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, bytes);
        File.SetLastWriteTimeUtc(file, modified ?? _now);
    }

    private void Write(Computer c, string path, string text, DateTime? modified = null) => Write(c, path, new UTF8Encoding(false).GetBytes(text), modified);

    private static byte[] ReadBytes(Computer c, string path) => File.ReadAllBytes(Path.Combine(c.Root, path));
    private static string ReadText(Computer c, string path) => File.ReadAllText(Path.Combine(c.Root, path));
    private static bool Exists(Computer c, string path) => File.Exists(Path.Combine(c.Root, path));

    private static byte[] Random(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new System.Random(seed).NextBytes(bytes);
        bytes[0] = 0xFF;      // never valid UTF-8
        return bytes;
    }

    private async Task<(Computer A, Computer B)> TwoSyncedComputers(params (string Path, byte[] Bytes)[] files)
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        foreach ((string path, byte[] bytes) in files) Write(a, path, bytes);
        Assert.Equal(SyncOutcome.Synced, (await Sync(a)).Outcome);
        Tick(1);
        Assert.Equal(SyncOutcome.Synced, (await Sync(b)).Outcome);
        Tick(1);
        return (a, b);
    }

    // ---- sharing a storage ---------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "A picture attached on one computer arrives on the other, byte for byte", Timeout = 30000)]
    public async Task A_picture_arrives_byte_for_byte()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        Write(a, "2026/11/shot.png", Png);
        Write(a, "2026/11/spec.pdf", Pdf);
        Write(a, "notes.md", "# Notes\n\nA text file among the pictures.\n");
        Write(a, "tools/events.log", "2026-11-12T08:00:00 | Ann | log | pasted\n");

        SyncReport up = await Sync(a);
        Assert.Equal(SyncOutcome.Synced, up.Outcome);
        Assert.Equal(4, up.Uploaded.Count);
        Tick(1);
        SyncReport down = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, down.Outcome);
        Assert.Equal(4, down.Downloaded.Count);
        Assert.Equal(Png, ReadBytes(b, "2026/11/shot.png"));
        Assert.Equal(Pdf, ReadBytes(b, "2026/11/spec.pdf"));
        Assert.Equal(ReadBytes(a, "notes.md"), ReadBytes(b, "notes.md"));
        Assert.Equal(ReadBytes(a, "tools/events.log"), ReadBytes(b, "tools/events.log"));
        Assert.True(File.Exists(Path.Combine(StorageFolder, "attachments", "manifest.json")));
        Assert.False(File.Exists(Path.Combine(StorageFolder, "manifest.json")));
    }

    [Fact(DisplayName = "Attachments and the board share one storage without touching each other's files", Timeout = 30000)]
    public async Task Attachments_and_the_board_share_one_storage()
    {
        IBoardStorage whole = BoardSyncApi.FolderStorage(StorageFolder);
        string boardA = Path.Combine(_temp.FullName, "boardA"), boardB = Path.Combine(_temp.FullName, "boardB");
        Directory.CreateDirectory(Path.Combine(boardA, "w"));
        File.WriteAllText(Path.Combine(boardA, "w", "proposal.md"), "# W\n\nThe board.\n");
        await BoardSyncApi.Sync(new SyncOptions(boardA, Path.Combine(_temp.FullName, "boardA-state"), whole, "A", () => _now));
        byte[] boardManifest = (await whole.Get("manifest.json"))!.Bytes;

        Computer a = NewComputer("A"), b = NewComputer("B");
        Write(a, "shot.png", Png);
        Tick(1);
        Assert.Equal(SyncOutcome.Synced, (await Sync(a)).Outcome);
        Tick(1);
        Assert.Equal(SyncOutcome.Synced, (await Sync(b)).Outcome);
        Assert.Equal(boardManifest, (await whole.Get("manifest.json"))!.Bytes);            // the attachments sync did not touch the board's manifest

        Tick(1);
        SyncReport board = await BoardSyncApi.Sync(new SyncOptions(boardB, Path.Combine(_temp.FullName, "boardB-state"), whole, "B", () => _now));
        Assert.Equal(SyncOutcome.Synced, board.Outcome);
        Assert.Equal(["w/proposal.md"], board.Downloaded);
        Assert.False(File.Exists(Path.Combine(boardB, "shot.png")));                  // no attachment came into the board
        Assert.False(Exists(b, "w/proposal.md"));                                      // and no board file into the attachments
        string attachmentsManifest = Encoding.UTF8.GetString((await whole.Get("attachments/manifest.json"))!.Bytes);
        Assert.Contains("shot.png", attachmentsManifest);
        Assert.DoesNotContain("proposal.md", attachmentsManifest);
        Assert.DoesNotContain("shot.png", Encoding.UTF8.GetString((await whole.Get("manifest.json"))!.Bytes));
        Assert.DoesNotContain(Prefix, Encoding.UTF8.GetString((await whole.Get("manifest.json"))!.Bytes));
    }

    // ---- the view ------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "A storage view under a prefix cannot read or write outside it", Timeout = 30000)]
    public async Task A_view_cannot_leave_its_prefix()
    {
        IBoardStorage inner = BoardSyncApi.FolderStorage(StorageFolder);
        await inner.Put("manifest.json", Encoding.UTF8.GetBytes("the board's manifest"), PutCondition.OnlyIfAbsent);
        await inner.Put("attachments2/other.bin", Png, PutCondition.OnlyIfAbsent);      // a sibling that merely starts with the same letters
        IBoardStorage view = BoardSyncApi.Under(inner, Prefix);

        // The same names inside the view are other files, never the board's.
        Assert.Null(await view.Get("manifest.json"));
        Assert.Null(await view.GetIfChanged("manifest.json", null));
        PutResult put = await view.Put("manifest.json", Encoding.UTF8.GetBytes("an attachment called manifest"), PutCondition.OnlyIfAbsent);
        Assert.True(put.Written);
        Assert.Equal("the board's manifest", Encoding.UTF8.GetString((await inner.Get("manifest.json"))!.Bytes));
        Assert.Equal("an attachment called manifest", Encoding.UTF8.GetString((await inner.Get("attachments/manifest.json"))!.Bytes));
        await view.Delete("manifest.json");
        Assert.NotNull(await inner.Get("manifest.json"));
        Assert.Null(await inner.Get("attachments/manifest.json"));

        // Paths that would climb out are refused, as they are without a view.
        foreach (string bad in new[] { "../manifest.json", "a/../../manifest.json", "/manifest.json", "..", "a\\b", "a//b", "./x", "" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => view.Get(bad));
            await Assert.ThrowsAsync<ArgumentException>(() => view.GetIfChanged(bad, null));
            await Assert.ThrowsAsync<ArgumentException>(() => view.Put(bad, Png, PutCondition.OnlyIfAbsent));
            await Assert.ThrowsAsync<ArgumentException>(() => view.Delete(bad));
        }
        foreach (string bad in new[] { "../", "..", "a/../b", "/", "a\\b", "a//b/c" })
            await Assert.ThrowsAsync<ArgumentException>(() => view.List(bad));
        Assert.False(File.Exists(Path.Combine(_temp.FullName, "manifest.json")));

        // A listing shows the view's files only, not the board's and not the sibling's.
        await view.Put("pic.png", Png, PutCondition.OnlyIfAbsent);
        Assert.Equal(["pic.png"], (await view.List("")).Select(e => e.Path).ToArray());
        Assert.Empty(await view.List("manifest.json"));

        // Prefixes are held to the same rules as paths.
        foreach (string bad in new[] { "", "..", "a/../b", "/a", "a\\b", "a//b", "a/./b", "a/", "../a", "C:/a" })
            Assert.Throws<ArgumentException>(() => BoardSyncApi.Under(inner, bad));
        Assert.Throws<ArgumentNullException>(() => BoardSyncApi.Under(null!, Prefix));

        // Views nest.
        IBoardStorage nested = BoardSyncApi.Under(BoardSyncApi.Under(inner, "a"), "b/c");
        await nested.Put("x.bin", Pdf, PutCondition.OnlyIfAbsent);
        Assert.NotNull(await inner.Get("a/b/c/x.bin"));
        Assert.Equal(["x.bin"], (await nested.List("")).Select(e => e.Path).ToArray());
        Assert.Equal(["c/x.bin"], (await BoardSyncApi.Under(inner, "a/b").List("")).Select(e => e.Path).ToArray());
    }

    [Fact(DisplayName = "A view under a prefix works over a folder and over S3, and lists paths relative to itself", Timeout = 30000)]
    public async Task A_view_works_over_both_providers()
    {
        FakeS3 fake = new();
        IBoardStorage s3 = BoardSyncApi.S3Storage("https://account.r2.example.test", FakeS3.Bucket, "boards/main", FakeS3.AccessKey, FakeS3.SecretKey, FakeS3.Region, fake);
        foreach (IBoardStorage inner in new[] { BoardSyncApi.FolderStorage(Path.Combine(_temp.FullName, "view-folder")), s3 })
        {
            IBoardStorage view = BoardSyncApi.Under(inner, Prefix);
            Assert.Equal(inner.ConditionalWrites, view.ConditionalWrites);
            await inner.Put("manifest.json", Encoding.UTF8.GetBytes("board"), PutCondition.OnlyIfAbsent);
            await inner.Put("attachments-old/z.bin", Png, PutCondition.OnlyIfAbsent);

            PutResult first = await view.Put("a.bin", Png, PutCondition.OnlyIfAbsent);
            Assert.True(first.Written);
            Assert.False((await view.Put("a.bin", Pdf, PutCondition.OnlyIfAbsent)).Written);
            await view.Put("dir/deep/b.bin", Pdf, PutCondition.OnlyIfAbsent);
            await view.Put("dir/c.bin", Random(300, 1), PutCondition.OnlyIfAbsent);

            StoredFile? got = await view.Get("a.bin");
            Assert.Equal(Png, got!.Bytes);
            Assert.Equal((await inner.Get("attachments/a.bin"))!.Version, got.Version);        // versions pass through
            Assert.True((await view.GetIfChanged("a.bin", got.Version))!.IsNotModified);
            Assert.False((await view.Put("a.bin", Pdf, PutCondition.OnlyIfVersion("wrong"))).Written);
            Assert.True((await view.Put("a.bin", Pdf, PutCondition.OnlyIfVersion(got.Version))).Written);
            Assert.Null(await view.Get("manifest.json"));

            Assert.Equal(["a.bin", "dir/c.bin", "dir/deep/b.bin"], (await view.List("")).Select(e => e.Path).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(["dir/c.bin", "dir/deep/b.bin"], (await view.List("dir/")).Select(e => e.Path).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(["dir/deep/b.bin"], (await view.List("dir/d")).Select(e => e.Path).ToArray());
            StoredEntry entry = (await view.List("")).First(e => e.Path == "a.bin");
            Assert.Equal((await inner.Get("attachments/a.bin"))!.Version, entry.Version);

            await view.Delete("a.bin");
            Assert.Null(await view.Get("a.bin"));
            Assert.Null(await inner.Get("attachments/a.bin"));
            Assert.NotNull(await inner.Get("manifest.json"));
        }
        Assert.Contains("board-bucket/boards/main/attachments/dir/deep/b.bin", fake.Objects.Keys);
    }

    // ---- the whole-file rule -------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "An attachment changed on both computers goes whole to the newer one and the other is kept in the trash", Timeout = 30000)]
    public async Task Both_changed_goes_whole_to_the_newer_one()
    {
        (Computer a, Computer b) = await TwoSyncedComputers(("pic.png", Png));
        byte[] fromA = Random(5000, 7), fromB = Random(6000, 8);
        Write(a, "pic.png", fromA, _now.AddMinutes(5));
        Write(b, "pic.png", fromB, _now.AddMinutes(10));
        Tick(20);

        SyncReport reportB = await Sync(b);                    // the first to sync: nothing on the storage changed yet
        Assert.Equal(["pic.png"], reportB.Uploaded);
        Tick(1);
        SyncReport reportA = await Sync(a);                    // both changed; the other computer's edit is the newer one

        Assert.Equal(SyncOutcome.Synced, reportA.Outcome);
        SyncConflict conflict = Assert.Single(reportA.Conflicts);
        Assert.Equal("pic.png", conflict.Path);
        Assert.True(conflict.DroppedHere);
        Assert.Empty(reportA.Merged);
        Assert.Equal(fromB, ReadBytes(a, "pic.png"));
        string kept = Assert.Single(reportA.Trashed);
        Assert.Equal(fromA, File.ReadAllBytes(kept));          // what this computer had is in the trash, byte for byte

        // The other way round: the local edit is the newer one, so it goes up whole and the storage's version stays among the storage's objects.
        Tick(1);
        byte[] later = Random(4000, 9);
        Write(a, "pic.png", later, _now);
        Write(b, "pic.png", Random(3000, 10), _now.AddMinutes(-5));
        Tick(1);
        Assert.Equal(["pic.png"], (await Sync(a)).Uploaded);
        Tick(1);
        SyncReport again = await Sync(b);
        Assert.Single(again.Conflicts);
        Assert.Equal(later, ReadBytes(b, "pic.png"));
    }

    [Fact(DisplayName = "With whole files only, a markdown file and an event log among the attachments are not merged as text", Timeout = 30000)]
    public async Task Text_files_are_not_merged_with_whole_files_only()
    {
        const string md = "# Notes\n\nline one\n", log = "2026-11-12T08:00:00 | Ann | log | start\n";
        (Computer a, Computer b) = await TwoSyncedComputers(("doc/notes.md", Encoding.UTF8.GetBytes(md)), ("doc/events.log", Encoding.UTF8.GetBytes(log)));
        Write(a, "doc/notes.md", md + "added on A\n", _now.AddMinutes(5));
        Write(a, "doc/events.log", log + "2026-11-12T08:05:00 | Ann | log | from A\n", _now.AddMinutes(5));
        Write(b, "doc/notes.md", md + "added on B\n", _now.AddMinutes(10));
        Write(b, "doc/events.log", log + "2026-11-12T08:10:00 | Bob | log | from B\n", _now.AddMinutes(10));
        Tick(20);
        await Sync(a);
        Tick(1);

        SyncReport report = await Sync(b);

        Assert.Empty(report.Merged);
        Assert.Equal(2, report.Conflicts.Count);
        Assert.Equal(md + "added on B\n", ReadText(b, "doc/notes.md"));                    // the newer whole file, no line of A's
        Assert.DoesNotContain("from A", ReadText(b, "doc/events.log"));
        Tick(1);
        await Sync(a);
        Assert.Equal(md + "added on B\n", ReadText(a, "doc/notes.md"));
        Assert.DoesNotContain("from A", ReadText(a, "doc/events.log"));
        Assert.Contains("from B", ReadText(a, "doc/events.log"));
    }

    [Fact(DisplayName = "With whole files only, text that looks like a git conflict never pauses the sync", Timeout = 30000)]
    public async Task Conflict_markers_never_pause_whole_files()
    {
        Computer a = NewComputer("A");
        string git = Path.Combine(_temp.FullName, "A", ".git");
        Directory.CreateDirectory(git);
        Write(a, "paste.md", "# T\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> branch\n");

        SyncReport blocked = await Sync(a, wholeFiles: false, git: git);
        Assert.Equal(SyncOutcome.Blocked, blocked.Outcome);
        Assert.Equal(SyncBlock.GitBusy, blocked.Blocked);

        SyncReport whole = await Sync(a, wholeFiles: true, git: git);
        Assert.Equal(SyncOutcome.Synced, whole.Outcome);
        Assert.Equal(["paste.md"], whole.Uploaded);
    }

    [Fact(DisplayName = "An attachment deleted on one computer goes to the trash on the other", Timeout = 30000)]
    public async Task A_deleted_attachment_goes_to_the_trash()
    {
        (Computer a, Computer b) = await TwoSyncedComputers(("pic.png", Png), ("keep.pdf", Pdf));
        File.Delete(Path.Combine(a.Root, "pic.png"));
        Tick(1);
        SyncReport up = await Sync(a);
        Assert.Equal(["pic.png"], up.DeletedInStorage);
        Tick(1);

        SyncReport down = await Sync(b);

        Assert.Equal(["pic.png"], down.DeletedHere);
        Assert.False(Exists(b, "pic.png"));
        Assert.True(Exists(b, "keep.pdf"));
        string trashed = Assert.Single(down.Trashed);
        Assert.Equal(Png, File.ReadAllBytes(trashed));
        Assert.StartsWith(Path.Combine(b.State, "trash"), trashed);
    }

    // ---- files that are too large --------------------------------------------------------------------------------------------------------

    private const int Cap = 300 * 1024;

    [Fact(DisplayName = "A file larger than the limit is skipped and reported, and the rest of the sync goes through", Timeout = 30000)]
    public async Task A_file_over_the_limit_is_skipped_and_reported()
    {
        using IDisposable cap = StorageLimits.OverrideObjectMax(Cap);
        Computer a = NewComputer("A"), b = NewComputer("B");
        Write(a, "small.png", Png);
        Write(a, "huge.bin", Random(Cap + 100 * 1024, 1));

        SyncReport first = await Sync(a);
        Assert.Equal(SyncOutcome.Synced, first.Outcome);
        Assert.Equal(["small.png"], first.Uploaded);
        Assert.Equal(["huge.bin"], first.TooLarge);
        Assert.Empty(first.DeletedInStorage);

        Tick(1);
        SyncReport second = await Sync(a);                      // still too large: listed again, nothing else happens
        Assert.Equal(SyncOutcome.NothingToDo, second.Outcome);
        Assert.Equal(["huge.bin"], second.TooLarge);
        Assert.True(Exists(a, "huge.bin"));

        Tick(1);
        SyncReport other = await Sync(b);
        Assert.Equal(["small.png"], other.Downloaded);
        Assert.Empty(other.TooLarge);
        Assert.False(Exists(b, "huge.bin"));

        // A file that was synced and then grew over the limit is not taken for a deleted one.
        Write(a, "small.png", Random(Cap + 1, 2), _now);
        Tick(1);
        SyncReport grown = await Sync(a);
        Assert.Equal(["huge.bin", "small.png"], grown.TooLarge);
        Assert.Empty(grown.DeletedInStorage);
        Assert.Empty(grown.Uploaded);
        Tick(1);
        SyncReport afterGrown = await Sync(b);
        Assert.Empty(afterGrown.DeletedHere);
        Assert.Equal(Png, ReadBytes(b, "small.png"));
    }

    [Fact(DisplayName = "A file that was too large is uploaded once it is small enough", Timeout = 30000)]
    public async Task A_file_that_shrank_is_uploaded()
    {
        using IDisposable cap = StorageLimits.OverrideObjectMax(Cap);
        Computer a = NewComputer("A"), b = NewComputer("B");
        Write(a, "huge.bin", Random(Cap + 50 * 1024, 3));
        Assert.Equal(["huge.bin"], (await Sync(a)).TooLarge);

        byte[] smaller = Random(Cap - 50 * 1024, 4);
        Tick(1);
        Write(a, "huge.bin", smaller);
        Tick(1);
        SyncReport report = await Sync(a);

        Assert.Equal(["huge.bin"], report.Uploaded);
        Assert.Empty(report.TooLarge);
        Tick(1);
        Assert.Equal(["huge.bin"], (await Sync(b)).Downloaded);
        Assert.Equal(smaller, ReadBytes(b, "huge.bin"));
    }

    [Fact(DisplayName = "An object in the storage that is larger than the limit is skipped and reported, not fatal", Timeout = 30000)]
    public async Task An_object_over_the_limit_is_skipped_and_reported()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        Write(a, "small.png", Png);
        Write(a, "big.bin", Random(Cap + 100 * 1024, 5));
        Assert.Equal(SyncOutcome.Synced, (await Sync(a)).Outcome);            // the limit is the default one here, so both go up
        Tick(1);

        SyncReport report;
        using (StorageLimits.OverrideObjectMax(Cap))                          // a computer with a stricter limit, or an object that grew
            report = await Sync(b);

        Assert.Equal(SyncOutcome.Synced, report.Outcome);
        Assert.Equal(["small.png"], report.Downloaded);
        Assert.Equal(["big.bin"], report.TooLarge);
        Assert.Equal(Png, ReadBytes(b, "small.png"));
        Assert.False(Exists(b, "big.bin"));

        Tick(1);
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(a)).Outcome);        // nothing was damaged in the storage
        Tick(1);
        SyncReport later = await Sync(b);                                     // with the limit as it was, the object comes down
        Assert.Equal(["big.bin"], later.Downloaded);
        Assert.Empty(later.TooLarge);
    }

    // ---- memory --------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Scanning a folder of large files does not read them all into memory at once", Timeout = 60000)]
    public async Task Scanning_does_not_hold_every_file()
    {
        Computer a = NewComputer("A"), b = NewComputer("B");
        const int files = 40, length = 600 * 1024;
        for (int i = 0; i < files; i++) Write(a, $"2026/11/photo-{i:D3}.jpg", Random(length, 100 + i));
        long largest = 0;
        SyncHooks hooks = new() { OnScanBuffer = size => largest = Math.Max(largest, size) };

        SyncReport up = await Sync(a, hooks: hooks);

        Assert.Equal(SyncOutcome.Synced, up.Outcome);
        Assert.Equal(files, up.Uploaded.Count);
        Assert.InRange(largest, 1, LocalTree.SmallFileMax);                 // never a buffer as big as one of the files
        Assert.True(largest < length);
        Tick(1);
        SyncReport down = await Sync(b);
        Assert.Equal(files, down.Downloaded.Count);
        for (int i = 0; i < files; i += 13) Assert.Equal(ReadBytes(a, $"2026/11/photo-{i:D3}.jpg"), ReadBytes(b, $"2026/11/photo-{i:D3}.jpg"));

        // A second sync of the unchanged folder reads nothing but the streaming buffer, too.
        largest = 0;
        Tick(1);
        Assert.Equal(SyncOutcome.NothingToDo, (await Sync(a, hooks: hooks)).Outcome);
        Assert.InRange(largest, 1, LocalTree.SmallFileMax);
    }
}

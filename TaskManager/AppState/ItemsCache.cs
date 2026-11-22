using OpenSpecStore;
using TaskManager.Helpers;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.AppState;

/// <summary>
/// The work items of each open repository. The list is assembled once per OpenSpecStore content version (own write, outside edit or repo switch), repository session,
/// commit and contributor source, and each change folder is parsed only when its files changed (name, size and modified time of each, from one directory pass that reads no file)
/// or the app wrote in it: the others keep their objects. A folder read again that says the same keeps its old item, so nothing that shows it redraws.
/// The module (WorkItems) parses a folder and applies its rules; what is kept, and until when, is decided here.
/// </summary>
public static partial class ItemsCache
{
    /// <summary>One change folder as last read: what its files looked like, what was parsed from them, and the item it showed as the last time (kept while nothing it came from moved).</summary>
    internal sealed class Folder(string dir, bool archived, string fingerprint, FolderRead read, bool racy)
    {
        internal readonly string Dir = dir;
        internal readonly bool Archived = archived;
        internal readonly string Fingerprint = fingerprint;
        internal readonly FolderRead Read = read;

        /// <summary>A file of the folder had a modified time within <see cref="Stamps.Slack"/> of the moment the folder was read: an edit after that read can share its size and time, so the folder is read once more, later (racy-git).</summary>
        internal readonly bool Racy = racy;

        private WorkItem? shown;
        private FolderRead? shownOf;
        private (int Id, string Token, int Epoch, string Statuses) shownFor;

        internal WorkItem Show(int id, string token, int epoch, string statusKey, Func<WorkItem> present)
        {
            if (shown is null || !ReferenceEquals(shownOf, Read) || shownFor != (id, token, epoch, statusKey))
            {
                shown = present();
                shownOf = Read;
                shownFor = (id, token, epoch, statusKey);
            }
            return shown;
        }

        /// <summary>The same folder with a new fingerprint: what was parsed says the same, so the item and its memo stay.</summary>
        internal Folder WithFingerprint(string fingerprint, bool racy)
        {
            Folder f = new(Dir, Archived, fingerprint, Read, racy) { shown = shown, shownOf = shownOf, shownFor = shownFor };
            return f;
        }
    }

    /// <summary>What one repository's work items were read from, and the board assembled from it.</summary>
    internal sealed class Entry
    {
        internal long Generation;
        internal long Version = -1;

        /// <summary>The store's count of outside changes when this was read: while it holds, the only changes since are the app's own writes (see <see cref="Patched"/>).</summary>
        internal long Outside = -1;
        internal string Git = "";
        internal int Epoch = -1;

        /// <summary>How far into OpenSpecStore's record of the app's own writes this cache has looked.</summary>
        internal long Cursor;

        internal List<Folder> Folders = [];
        internal IReadOnlyList<string> Statuses = [];

        /// <summary>The board was painted from the saved index and no listing has checked it yet (<see cref="Verify"/> does).</summary>
        internal bool Unverified;

        /// <summary>Folders were read, changed or dropped since the index was last saved or loaded.</summary>
        internal bool Dirty;

        internal BoardRead? Board;
        internal IReadOnlyList<WorkItem> Visible = [];
    }

    internal sealed class State
    {
        public readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
        public readonly object Gate = new();
        public int Loads, FolderReads, Passes, Patches;

        /// <summary>The time a read is stamped with (a test sets it: a folder is "racy" when a file's modified time is within a few milliseconds of it).</summary>
        public Func<DateTime> Clock = () => DateTime.UtcNow;

        /// <summary>Where the index of each repository is kept between runs; null = nowhere (the tests' default: only the app, and a test about the index, set it).</summary>
        public string? IndexFolder;

        /// <summary>Per project: the FolderStamp the last duplicate-number scan saw.</summary>
        public readonly Dictionary<string, string> Scans = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>How many times the board was assembled again (a test counts them).</summary>
    public static int Loads => Caches.S.Items.Loads;

    /// <summary>How many change folders were looked at in full and read again, because their files looked different or the app wrote in them (a test counts them).</summary>
    public static int FolderReads => Caches.S.Items.FolderReads;

    /// <summary>The visible work items of the repository in scope (hidden ones too with <paramref name="includeHidden"/>); the same list instance while nothing moved.</summary>
    public static IReadOnlyList<WorkItem> All(bool includeHidden = false)
    {
        State s = Caches.S.Items;
        lock (s.Gate)
        {
            BoardRead board = Board();
            if (includeHidden) return board.Items;
            Entry e = s.Entries[OpenSpecStoreApi.RepoRoot ?? ""];
            return e.Visible;
        }
    }

    public static WorkItem? Get(int id) => Board().Items.FirstOrDefault(w => w.Id == id);

    /// <summary>How many items the bodies cache holds at most (descriptions, events and notes of the items used last); the one used longest ago makes room.</summary>
    public static int BodyBound => BodyCache.Bound;

    /// <summary>How many items the bodies cache holds now (a test counts them).</summary>
    public static int BodyCount => Caches.S.Bodies.Count;

    /// <summary>
    /// The description (proposal body) of an item: read when a page asks, kept for the items used last, and read again only when proposal.md's size or modified time differ from what was read
    /// (or it was read within a few milliseconds of its time, see <see cref="Stamps"/>).
    /// </summary>
    public static string Description(int id)
    {
        if (Get(id) is not WorkItem item) return "";
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        long generation = OpenSpecStoreApi.RepoGeneration;
        ItemBodies bodies = Caches.S.Bodies.Of(root, id);
        (string stamp, long newest) = Stamps.Of(Path.Combine(item.Path, "proposal.md"));
        if (bodies.Description is { } held && held.Holds(generation, stamp)) return held.Value;
        DateTime readAt = Caches.S.Items.Clock();   // taken before reading, so a write during the read shows as racy
        string text = WorkItemsApi.ReadDescription(id);
        bodies.Description = new(generation, stamp, newest, readAt, text);
        return text;
    }

    /// <summary>The worklog events of an item: read when a page asks, kept for the items used last and read again only when events.log's size or modified time differ; an equal list keeps its instance.</summary>
    public static IReadOnlyList<WorkItemEvent> Events(int id)
    {
        if (Get(id) is not WorkItem item) return [];
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        long generation = OpenSpecStoreApi.RepoGeneration;
        ItemBodies bodies = Caches.S.Bodies.Of(root, id);
        (string stamp, long newest) = Stamps.Of(Path.Combine(item.Path, "events.log"));
        if (bodies.Events is { } held && held.Holds(generation, stamp)) return held.Value;
        DateTime readAt = Caches.S.Items.Clock();
        IReadOnlyList<WorkItemEvent> read = Same.Keep(bodies.Events?.Value, WorkItemsApi.ReadEvents(id));
        bodies.Events = new(generation, stamp, newest, readAt, read);
        return read;
    }

    /// <summary>What the board was the last time it was read, without reading it again (null before the first read, or after the repository was reopened).</summary>
    public static BoardRead? LastSeen()
    {
        State s = Caches.S.Items;
        lock (s.Gate) return s.Entries.TryGetValue(OpenSpecStoreApi.RepoRoot ?? "", out Entry? e) && e.Generation == OpenSpecStoreApi.RepoGeneration ? e.Board : null;
    }

    /// <summary>The board of the repository in scope: the items in board order and the folders they were read from.</summary>
    public static BoardRead Board()
    {
        State s = Caches.S.Items;
        lock (s.Gate)
        {
            long outside = OpenSpecStoreApi.OutsideVersion;   // before the version: an outside change that lands between the two shows as a mismatch next time, never as a match
            long version = OpenSpecStoreApi.Version;
            string root = OpenSpecStoreApi.RepoRoot ?? "";
            long generation = OpenSpecStoreApi.RepoGeneration;   // a repository closed and opened again starts empty: the store forgot what the app had read of it
            var git = WorkItemsApi.ContributorSource();
            string token = git?.Token ?? "";
            int epoch = WorkItemsApi.ContributorEpoch;
            Entry e = EntryOf(s, root, generation);
            if (e.Board is not null && e.Version == version && e.Git == token && e.Epoch == epoch) return e.Board;

            (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreApi.ChangesSince(e.Cursor);   // asked before reading: a write that lands meanwhile shows next time
            using (System.Diagnostics.Activity? read = AppLog.Start("board", "read"))
            {
                List<Folder> held = e.Folders;
                List<Folder>? patched = e.Unverified ? null : Patched(s, e, root, touched, outside);   // a board painted from the saved index is checked against the disk first
                read?.SetTag("result", patched is null ? "pass" : "patch");
                e.Folders = patched ?? ReadFolders(s, e.Folders, touched);
                if (!held.SequenceEqual(e.Folders)) e.Dirty = true;   // a folder was read again, or came or went: the saved index is behind
                e.Unverified = false;
            }

            List<FolderRead> reads = [.. e.Folders.Select(f => f.Read)];
            IReadOnlyList<int> ids = WorkItemsApi.Number(reads);   // changes without a stored number get a provisional one, written on their first edit
            IReadOnlyList<string> statuses = WorkItemsApi.GetStatuses();   // the project's list: an item with a status outside it (a pull brought it) shows in Backlog
            string statusKey = string.Join('\n', statuses);
            e.Statuses = statuses;
            List<WorkItem> items = [.. e.Folders.Select((f, i) => f.Show(ids[i], token, epoch, statusKey, () => WorkItemsApi.Present(f.Read, ids[i], statuses, git)))];

            BoardRead board = new(items, reads);
            if (e.Board is { } old && old.Items.Count == items.Count && old.Items.Zip(items).All(p => ReferenceEquals(p.First, p.Second)) && old.Folders.Zip(reads).All(p => ReferenceEquals(p.First, p.Second)))
                board = old;   // nothing that shows changed: the same board, so nothing redraws
            else e.Visible = [.. items.Where(w => !w.Hidden)];
            (e.Version, e.Outside, e.Git, e.Epoch, e.Cursor, e.Board) = (version, outside, token, epoch, cursor, board);
            s.Loads++;
            return board;
        }
    }


    private static Entry EntryOf(State s, string root, long generation)
    {
        if (!s.Entries.TryGetValue(root, out Entry? e) || e.Generation != generation) s.Entries[root] = e = new Entry { Generation = generation };
        return e;
    }

    /// <summary>The change folders in board order, each from <paramref name="old"/> when nothing in it changed and else read again; folders that are gone drop out.</summary>
    private static List<Folder> ReadFolders(State s, List<Folder> old, IReadOnlyList<string> touched)
    {
        Dictionary<string, Folder> before = old.ToDictionary(f => f.Dir, StringComparer.OrdinalIgnoreCase);
        List<Folder> now = [];
        s.Passes++;
        foreach ((string dir, bool archived) in WorkItemsApi.ListFolders())
        {
            try { if (Fresh(s, dir, archived, before.GetValueOrDefault(dir), touched) is Folder folder) now.Add(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // being moved or written while looked at: left out of this read, the watcher's reload is the retry
        }
        return now;
    }

    /// <summary>
    /// The folder: <paramref name="old"/> when its files look the same and the app did not write in it, else read again. The look of a folder is the name, size and modified time
    /// of every file in it, from one directory pass that reads no file, taken before reading, so a write during the read shows as a change next time.
    /// </summary>
    private static Folder? Fresh(State s, string dir, bool archived, Folder? old, IReadOnlyList<string> touched)
    {
        (string fingerprint, long newest) = Fingerprint(dir);
        DateTime readAt = s.Clock();   // taken before reading, so a write during the read shows as racy
        if (old is not null && old.Archived == archived && old.Fingerprint == fingerprint && !old.Racy && !Written(dir, touched)) return old;
        s.FolderReads++;
        if (WorkItemsApi.ReadFolder(dir, archived) is not FolderRead read) return null;
        bool racy = Stamps.Racy(newest, readAt);
        // The files were touched but say the same (a save with no change, a checkout that rewrote them: same names and sizes, only the times differ): the old item stays.
        // A file whose size changed (a note's body, which the item itself does not show) is a change of the folder: a new item, as the folder's revision used to make it.
        if (old is not null && old.Archived == archived && Sizes(old.Fingerprint) == Sizes(fingerprint) && Same.Value(old.Read.Item, read.Item) && old.Read.StoredId == read.StoredId) return old.WithFingerprint(fingerprint, racy);
        return new Folder(dir, archived, fingerprint, read, racy);
    }

    /// <summary>A fingerprint without the modified times: the names and sizes of the folder's files.</summary>
    private static string Sizes(string fingerprint) =>
        string.Join('\n', fingerprint.Split('\n').Select(l => l.Split('|') is { Length: 3 } p ? p[0] + "|" + p[1] : l));

    /// <summary>
    /// The look of a folder (names, sizes and modified times of its files, one line each: <c>relative/path|size|UTC ticks</c>, a folder just <c>relative/path</c>) and the newest modified time in it.
    /// Paths use forward slashes and the lines are sorted, so the same folder looks the same on every platform and in every run (the saved index keeps these lines).
    /// </summary>
    private static (string Fingerprint, long Newest) Fingerprint(string dir)
    {
        List<string> lines = [];
        long newest = 0;
        foreach (FileSystemInfo info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            string relative = info.FullName[(dir.Length + 1)..];
            if (Path.DirectorySeparatorChar != '/') relative = relative.Replace(Path.DirectorySeparatorChar, '/');
            if (info is FileInfo f) { lines.Add(relative + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks); newest = Math.Max(newest, f.LastWriteTimeUtc.Ticks); }
            else lines.Add(relative);
        }
        lines.Sort(StringComparer.Ordinal);
        return (lines.Count == 0 ? "" : string.Join('\n', lines) + "\n", newest);
    }

    /// <summary>True when the app itself wrote, deleted or moved this folder or something in it (the store keeps that record).</summary>
    internal static bool Written(string dir, IReadOnlyList<string> touched) =>
        touched.Any(p => p.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && (p.Length == dir.Length || p[dir.Length] is '\\' or '/'));

    /// <summary>The folder stamp the last duplicate-number scan of the project saw; null when it was never scanned (or <see cref="Rescan"/> asked for another).</summary>
    public static string? ScannedStamp(string project)
    {
        State s = Caches.S.Items;
        lock (s.Gate) return s.Scans.GetValueOrDefault(project);
    }

    /// <summary>Remembers the stamp the scan of the project saw: no scan until the change folders move again.</summary>
    public static void Scanned(string project, string stamp)
    {
        State s = Caches.S.Items;
        lock (s.Gate) s.Scans[project] = stamp;
    }

    /// <summary>A project being opened is scanned for duplicate numbers once more.</summary>
    public static void Rescan(string project)
    {
        State s = Caches.S.Items;
        lock (s.Gate) s.Scans.Remove(project);
    }

    // ── The index kept between runs ──

    /// <summary>The folder the index of each repository is kept in (the app's data folder); null = none is kept. The app sets it at start.</summary>
    public static void UseIndex(string? folder)
    {
        State s = Caches.S.Items;
        lock (s.Gate) s.IndexFolder = folder;
    }

    /// <summary>
    /// Paints the repository in scope from its saved index: the board is what it was at the last save, with no listing and no file read, until <see cref="Verify"/> checks it. False when there is no index that fits
    /// (none saved, another format version, another place, unreadable: it is dropped) or the repository was already read in this run. Call it inside the repository's scope, right after it is opened.
    /// </summary>
    internal static bool TryLoadIndex(string root)
    {
        State s = Caches.S.Items;
        if (s.IndexFolder is not string folder) return false;
        string repoRoot = OpenSpecStoreApi.RepoRoot ?? root;
        IndexData? data = IndexFile.Load(folder, repoRoot);
        if (data is null) return false;
        lock (s.Gate)
        {
            long generation = OpenSpecStoreApi.RepoGeneration;
            if (s.Entries.TryGetValue(repoRoot, out Entry? have) && have.Generation == generation && have.Board is not null) return false;
            List<Folder> folders = [.. data.Folders.Select(f => Restored(repoRoot, f))];
            Entry e = EntryOf(s, repoRoot, generation);
            long version = OpenSpecStoreApi.Version;
            var git = WorkItemsApi.ContributorSource();
            string token = git?.Token ?? "";
            int epoch = WorkItemsApi.ContributorEpoch;
            e.Folders = folders;
            List<FolderRead> reads = [.. folders.Select(f => f.Read)];
            IReadOnlyList<int> ids = WorkItemsApi.Number(reads);
            IReadOnlyList<string> statuses = data.Statuses.Count > 0 ? data.Statuses : WorkItemsApi.Statuses;
            string statusKey = string.Join('\n', statuses);
            List<WorkItem> items = [.. folders.Select((f, i) => f.Show(ids[i], token, epoch, statusKey, () => WorkItemsApi.Present(f.Read, ids[i], statuses, git)))];
            e.Board = new BoardRead(items, reads);
            e.Visible = [.. items.Where(w => !w.Hidden)];
            e.Statuses = statuses;
            (e.Version, e.Git, e.Epoch, e.Cursor) = (version, token, epoch, OpenSpecStoreApi.ChangesSince(long.MaxValue).Cursor);
            e.Unverified = true;
            e.Dirty = false;
            return true;
        }
    }

    private static Folder Restored(string repoRoot, IndexFolder f)
    {
        string dir = Path.Combine(repoRoot, f.Path.Replace('/', Path.DirectorySeparatorChar));
        FolderRead read = new() { Dir = dir, Archived = f.Archived, StoredId = f.StoredId, Item = WorkItemsApi.Restore(f.Item with { Path = dir }) };
        string fingerprint = string.Concat(f.Stamps.Select(st => st.Size is long size && st.Mtime is string at
            ? st.Path + "|" + size + "|" + DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime().Ticks + "\n"
            : st.Path + "\n"));
        return new Folder(dir, f.Archived, fingerprint, read, f.Racy);
    }

    /// <summary>
    /// Checks a board painted from the index against the disk: one listing of the repository's change folders, each compared to its saved stamps, and only a folder that differs is read again (the work of any
    /// board read, which does it by itself when the repository's version moves). Nothing special happens for a repository that was not painted from an index: it is read as usual.
    /// </summary>
    internal static void Verify()
    {
        State s = Caches.S.Items;
        lock (s.Gate)
            if (s.Entries.TryGetValue(OpenSpecStoreApi.RepoRoot ?? "", out Entry? e) && e.Generation == OpenSpecStoreApi.RepoGeneration && e.Unverified) e.Version = -1;   // the next read goes through the folders
        _ = Board();
    }

    /// <summary>
    /// Saves the index of the repository in scope when something was read, changed or dropped since it was last saved or loaded (<paramref name="force"/>: always). Nothing is saved for a board that was only painted
    /// from the index and not verified, nor while no index folder is set. A failed write leaves the old file; the index is a cache. Returns whether a file was written.
    /// </summary>
    internal static bool SaveIndex(string root, bool force = false)
    {
        State s = Caches.S.Items;
        IndexData data;
        string folder;
        lock (s.Gate)
        {
            if (s.IndexFolder is not string where) return false;
            string repoRoot = OpenSpecStoreApi.RepoRoot ?? root;
            if (!s.Entries.TryGetValue(repoRoot, out Entry? e) || e.Generation != OpenSpecStoreApi.RepoGeneration || e.Board is null || e.Unverified || !(e.Dirty || force)) return false;
            folder = where;
            data = new IndexData
            {
                Version = IndexFile.FormatVersion,
                Repo = Path.GetFullPath(repoRoot),
                SavedAt = s.Clock().ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                Statuses = [.. e.Statuses],
                Folders = [.. e.Folders.Where(f => f.Read.Item.Warning is null).Select(f => Saved(repoRoot, f))],   // a folder with a warning is read again at every start: its message may name paths
            };
            e.Dirty = false;
        }
        try { IndexFile.Save(folder, data); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (s.Gate) { if (s.Entries.TryGetValue(OpenSpecStoreApi.RepoRoot ?? root, out Entry? e)) e.Dirty = true; }
            return false;
        }
    }

    private static IndexFolder Saved(string repoRoot, Folder f) => new()
    {
        Path = Path.GetRelativePath(repoRoot, f.Dir).Replace('\\', '/'),
        Archived = f.Archived,
        Racy = f.Racy,
        StoredId = f.Read.StoredId,
        Item = f.Read.Item with { Path = "" },
        Stamps = [.. f.Fingerprint.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('|') is { Length: 3 } p
            ? new IndexStamp { Path = p[0], Size = long.Parse(p[1]), Mtime = new DateTime(long.Parse(p[2]), DateTimeKind.Utc).ToString("O", System.Globalization.CultureInfo.InvariantCulture) }
            : new IndexStamp { Path = line })],
    };

    /// <summary>The safety valve: drops the saved index of the repository in scope, reads every change folder again and saves a fresh one.</summary>
    internal static void RebuildIndex(string root)
    {
        State s = Caches.S.Items;
        string? folder;
        lock (s.Gate) folder = s.IndexFolder;
        if (folder is not null) IndexFile.Delete(folder, OpenSpecStoreApi.RepoRoot ?? root);
        Invalidate(OpenSpecStoreApi.RepoRoot ?? root);
        _ = Board();
        SaveIndex(root, force: true);
    }

    /// <summary>Forgets what was read of a repository (all of them without a root): the next read goes to disk.</summary>
    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.Items;
        lock (s.Gate) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(root); }
        Caches.S.Bodies.Clear(root);   // and the bodies read of its items
    }
}

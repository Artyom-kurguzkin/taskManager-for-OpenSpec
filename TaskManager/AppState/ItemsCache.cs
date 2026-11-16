using OpenSpecStore;
using WorkItems;
using WorkItems.PublicDTOs;

namespace TaskManager.AppState;

/// <summary>
/// The work items of each open repository. The list is assembled once per OpenSpecStore content version (own write, outside edit or repo switch), repository session,
/// commit and contributor source, and each change folder is parsed only when its files changed (name, size and modified time of each, from one directory pass that reads no file)
/// or the app wrote in it: the others keep their objects. A folder read again that says the same keeps its old item, so nothing that shows it redraws.
/// The module (WorkItems) parses a folder and applies its rules; what is kept, and until when, is decided here.
/// </summary>
public static class ItemsCache
{
    /// <summary>One change folder as last read: what its files looked like, what was parsed from them, and the item it showed as the last time (kept while nothing it came from moved).</summary>
    internal sealed class Folder(string dir, bool archived, string fingerprint, FolderRead read)
    {
        internal readonly string Dir = dir;
        internal readonly bool Archived = archived;
        internal readonly string Fingerprint = fingerprint;
        internal readonly FolderRead Read = read;

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
        internal Folder WithFingerprint(string fingerprint)
        {
            Folder f = new(Dir, Archived, fingerprint, Read) { shown = shown, shownOf = shownOf, shownFor = shownFor };
            return f;
        }
    }

    /// <summary>What one repository's work items were read from, and the board assembled from it.</summary>
    internal sealed class Entry
    {
        internal long Generation;
        internal long Version = -1;
        internal string Git = "";
        internal int Epoch = -1;

        /// <summary>How far into OpenSpecStore's record of the app's own writes this cache has looked.</summary>
        internal long Cursor;

        internal List<Folder> Folders = [];
        internal BoardRead? Board;
        internal IReadOnlyList<WorkItem> Visible = [];
        internal readonly Dictionary<int, (long Generation, long Version, IReadOnlyList<WorkItemEvent> Events)> Events = [];
    }

    internal sealed class State
    {
        public readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
        public readonly object Gate = new();
        public int Loads, FolderReads;

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

    /// <summary>The worklog events of an item, read again only when the board's content or the repository session moved; an equal list keeps its instance.</summary>
    public static IReadOnlyList<WorkItemEvent> Events(int id)
    {
        State s = Caches.S.Items;
        long version = OpenSpecStoreApi.Version, generation = OpenSpecStoreApi.RepoGeneration;
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        lock (s.Gate)
        {
            Entry e = EntryOf(s, root, generation);
            if (e.Events.TryGetValue(id, out var hit) && hit.Generation == generation && hit.Version == version) return hit.Events;
            IReadOnlyList<WorkItemEvent> read = WorkItemsApi.GetEvents(id);
            read = Same.Keep(e.Events.TryGetValue(id, out var before) ? before.Events : null, read);
            e.Events[id] = (generation, version, read);
            return read;
        }
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
            long version = OpenSpecStoreApi.Version;
            string root = OpenSpecStoreApi.RepoRoot ?? "";
            long generation = OpenSpecStoreApi.RepoGeneration;   // a repository closed and opened again starts empty: the store forgot what the app had read of it
            var git = WorkItemsApi.ContributorSource();
            string token = git?.Token ?? "";
            int epoch = WorkItemsApi.ContributorEpoch;
            Entry e = EntryOf(s, root, generation);
            if (e.Board is not null && e.Version == version && e.Git == token && e.Epoch == epoch) return e.Board;

            (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreApi.ChangesSince(e.Cursor);   // asked before reading: a write that lands meanwhile shows next time
            e.Folders = ReadFolders(s, e.Folders, touched);

            List<FolderRead> reads = [.. e.Folders.Select(f => f.Read)];
            IReadOnlyList<int> ids = WorkItemsApi.Number(reads);   // changes without a stored number get a provisional one, written on their first edit
            IReadOnlyList<string> statuses = WorkItemsApi.GetStatuses();   // the project's list: an item with a status outside it (a pull brought it) shows in Backlog
            string statusKey = string.Join('\n', statuses);
            List<WorkItem> items = [.. e.Folders.Select((f, i) => f.Show(ids[i], token, epoch, statusKey, () => WorkItemsApi.Present(f.Read, ids[i], statuses, git)))];

            BoardRead board = new(items, reads);
            if (e.Board is { } old && old.Items.Count == items.Count && old.Items.Zip(items).All(p => ReferenceEquals(p.First, p.Second)) && old.Folders.Zip(reads).All(p => ReferenceEquals(p.First, p.Second)))
                board = old;   // nothing that shows changed: the same board, so nothing redraws
            else e.Visible = [.. items.Where(w => !w.Hidden)];
            (e.Version, e.Git, e.Epoch, e.Cursor, e.Board) = (version, token, epoch, cursor, board);
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
        string fingerprint = Fingerprint(dir);
        if (old is not null && old.Archived == archived && old.Fingerprint == fingerprint && !Written(dir, touched)) return old;
        s.FolderReads++;
        if (WorkItemsApi.ReadFolder(dir, archived) is not FolderRead read) return null;
        // The files were touched but say the same (a save with no change, a checkout that rewrote them): the old item stays.
        if (old is not null && old.Archived == archived && read.Item.Revision.Length > 0 && Same.Value(old.Read.Item, read.Item) && old.Read.StoredId == read.StoredId) return old.WithFingerprint(fingerprint);
        return new Folder(dir, archived, fingerprint, read);
    }

    private static string Fingerprint(string dir)
    {
        System.Text.StringBuilder sb = new();
        foreach (FileSystemInfo info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            sb.Append(info.FullName, dir.Length, info.FullName.Length - dir.Length);
            if (info is FileInfo f) sb.Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks);
            sb.Append('\n');
        }
        return sb.ToString();
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

    /// <summary>Forgets what was read of a repository (all of them without a root): the next read goes to disk.</summary>
    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.Items;
        lock (s.Gate) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(root); }
    }
}

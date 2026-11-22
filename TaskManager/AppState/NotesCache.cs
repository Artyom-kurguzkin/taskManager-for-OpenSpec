using Microsoft.Extensions.Logging;
using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;
using TaskManager.Helpers;

namespace TaskManager.AppState;

/// <summary>
/// The notes of each open repository, kept per item (one entry per change's notes/ folder, with a stamp of its files: names, sizes, modified times) and assembled from those entries.
/// A change that names its item (the app's own writes are recorded by path; <see cref="Refresh(string, int)"/> names one) looks at that item's folder alone: the other items' folders are
/// neither listed nor read, their entries and parsed notes stay. Only when something moved behind the app's back without saying where (the watcher, <see cref="Refresh(string)"/>) is every
/// folder stamped again, and then only the ones whose stamp differs are read. The module (Notes) reads a folder and settles the id collisions; what is kept, and until when, is decided here.
/// </summary>
public static class NotesCache
{
    internal sealed class Entry
    {
        internal long Generation;
        internal long Version = -1;
        internal long Cursor;
        /// <summary>The store's outside version at the last pass over the folders: when it moved, something changed without naming its item and every folder is stamped again.</summary>
        internal long Outside = -1;
        /// <summary>A folder was patched by <see cref="Refresh(string, int)"/>: the notes are assembled again from the entries, without listing the others.</summary>
        internal bool Dirty;
        /// <summary>Notes folders that did not exist at the last pass (so an item without notes is not asked about again until something names it).</summary>
        internal HashSet<string> Absent = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, (string Fingerprint, NotesFolderRead Folder)> Folders = new(StringComparer.OrdinalIgnoreCase);
        internal NotesRead? Read;

        /// <summary>Renumberings made by loads and not yet handed to <see cref="TakeRenumbered"/>.</summary>
        internal List<(int OldId, int NewId, string Path)> Renumbered = [];
    }

    internal sealed class State
    {
        public readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
        public readonly object Gate = new();
        public int Loads;
        /// <summary>A test seam: when set, the notes folder of every item listed (its files' names, sizes and times looked at) is added to it. Null in the app.</summary>
        public List<string>? ListLog;
    }

    /// <summary>A test seam: the notes folders the cache listed (one entry per listing), to prove a change to one item looks at that item's folder alone.</summary>
    public static List<string>? ListLog { get => Caches.S.Notes.ListLog; set => Caches.S.Notes.ListLog = value; }

    /// <summary>How many times the notes were assembled again (a test counts them).</summary>
    public static int Loads => Caches.S.Notes.Loads;

    /// <summary>All notes of the repository in scope, and the highest note number seen anywhere. Reading also settles id collisions (see NotesApi.Settle).</summary>
    public static NotesRead Get()
    {
        State s = Caches.S.Notes;
        lock (OpenSpecStoreApi.StepsGate)   // never mid-way through a merge: see StepsGate
        lock (s.Gate)
        {
            long version = OpenSpecStoreApi.Version;
            string root = OpenSpecStoreApi.RepoRoot ?? "";
            long generation = OpenSpecStoreApi.RepoGeneration;
            if (!s.Entries.TryGetValue(root, out Entry? e) || e.Generation != generation) s.Entries[root] = e = new Entry { Generation = generation };
            if (e.Read is not null && e.Version == version && !e.Dirty) return e.Read;

            long outside = OpenSpecStoreApi.OutsideVersion;   // asked before looking: an outside change that lands meanwhile shows next time
            (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreApi.ChangesSince(e.Cursor);   // asked before reading: a write that lands meanwhile shows next time
            bool everything = e.Read is null || e.Outside != outside;   // something changed behind the app's back, nobody says where: every folder is stamped again
            Dictionary<string, (string Fingerprint, NotesFolderRead Folder)> now = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> absent = new(StringComparer.OrdinalIgnoreCase);
            List<NotesFolderRead> folders = [];
            foreach (WorkItems.PublicDTOs.WorkItem wi in ItemsCache.All(includeHidden: true))
            {
                string dir = Path.Combine(wi.Path, "notes");
                bool written = ItemsCache.Written(dir, touched);
                if (!everything && !written)   // nobody named this item: what was read of it stands, no listing
                {
                    if (e.Folders.TryGetValue(dir, out var kept))
                    {
                        NotesApi.Rebind(kept.Folder, wi.Id);   // a provisional number moved: no file to read again, only the owner of the notes changes
                        now[dir] = kept;
                        folders.Add(kept.Folder);
                        continue;
                    }
                    if (e.Absent.Contains(dir)) { absent.Add(dir); continue; }
                }
                DirectoryInfo info = new(dir);
                Listed(s, dir);
                if (!info.Exists) { absent.Add(dir); continue; }
                string fingerprint = Fingerprint(info);   // taken before reading, so a write during the read shows as a change next time
                NotesFolderRead folder;
                if (e.Folders.TryGetValue(dir, out var old) && old.Fingerprint == fingerprint && !written)
                {
                    folder = old.Folder;
                    NotesApi.Rebind(folder, wi.Id);
                }
                else folder = ReadFolder(dir, wi.Id);
                now[dir] = (fingerprint, folder);
                folders.Add(folder);
            }
            (e.Absent, e.Outside, e.Dirty) = (absent, outside, false);
            e.Folders = now;
            NotesRead read = NotesApi.Settle(folders, out IReadOnlyList<(int OldId, int NewId, string Path)> renumbered);
            e.Renumbered.AddRange(renumbered);
            if (read.Wrote) version = OpenSpecStoreApi.Version;   // our own renames and rewrites are not a reason to read again
            if (e.Read is { } before && before.MaxId == read.MaxId && before.Notes.Count == read.Notes.Count && before.Notes.Zip(read.Notes).All(p => ReferenceEquals(p.First, p.Second))) read = before;
            (e.Version, e.Cursor, e.Read) = (version, cursor, read);
            s.Loads++;
            return read;
        }
    }

    /// <summary>A notes folder read from disk, said at Debug (RTM_LOG=Debug) so a run log shows which folders a change reads.</summary>
    private static NotesFolderRead ReadFolder(string dir, int workItemId)
    {
        AppLog.Logger(nameof(NotesCache)).LogDebug("notes folder read {Folder} (#wi{Id})", dir, workItemId);
        return NotesApi.ReadFolder(dir, workItemId);
    }

    private static void Listed(State s, string dir)
    {
        AppLog.Logger(nameof(NotesCache)).LogDebug("notes folder listed {Folder}", dir);
        if (s.ListLog is { } listed) lock (listed) listed.Add(dir);
    }

    /// <summary>The look of a notes folder's files: names, sizes and modified times, in a fixed order.</summary>
    private static string Fingerprint(DirectoryInfo info) =>
        string.Join('\n', info.GetFiles("n*.md").OrderBy(f => f.FullName, StringComparer.CurrentCulture).Select(f => $"{f.Name}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"));

    /// <summary>
    /// One item's notes folder changed (a verification found it, or a tool wrote it): its folder alone is listed, and read again when its stamp differs; the next <see cref="Get()"/> assembles the notes from the entries
    /// without listing the other folders. An item the cache does not know (new, moved, gone) or another repository asks for a stamp of every folder at the next read. Nothing is read when nothing is held yet.
    /// </summary>
    public static void Refresh(string root, int id)
    {
        State s = Caches.S.Notes;
        lock (OpenSpecStoreApi.StepsGate)
        lock (s.Gate)
        {
            if (!s.Entries.TryGetValue(root, out Entry? e) || e.Generation != OpenSpecStoreApi.RepoGeneration || e.Read is null) return;
            if (!string.Equals(root, OpenSpecStoreApi.RepoRoot, StringComparison.OrdinalIgnoreCase) || ItemsCache.Get(id) is not WorkItems.PublicDTOs.WorkItem wi) { e.Outside = -1; return; }
            string dir = Path.Combine(wi.Path, "notes");
            DirectoryInfo info = new(dir);
            Listed(s, dir);
            if (!info.Exists) { if (e.Folders.Remove(dir) | e.Absent.Add(dir)) e.Dirty = true; return; }
            string fingerprint = Fingerprint(info);
            if (e.Folders.TryGetValue(dir, out var old) && old.Fingerprint == fingerprint) return;   // the same files: nothing to read
            e.Folders[dir] = (fingerprint, ReadFolder(dir, wi.Id));
            e.Absent.Remove(dir);
            e.Dirty = true;
        }
    }

    /// <summary>Something may have changed in the repository's notes without saying where: the next read stamps every notes folder and reads the ones whose stamp differs.</summary>
    public static void Refresh(string root)
    {
        State s = Caches.S.Notes;
        lock (s.Gate) if (s.Entries.TryGetValue(root, out Entry? e)) { e.Outside = -1; e.Dirty = true; }
    }

    /// <summary>The renumberings made by reads since the last call (empty when there were none); reads the notes first, which settles them.</summary>
    public static IReadOnlyList<(int OldId, int NewId, string Path)> TakeRenumbered()
    {
        State s = Caches.S.Notes;
        lock (OpenSpecStoreApi.StepsGate)
        lock (s.Gate)
        {
            Get();
            if (!s.Entries.TryGetValue(OpenSpecStoreApi.RepoRoot ?? "", out Entry? e) || e.Renumbered.Count == 0) return [];
            List<(int, int, string)> made = e.Renumbered;
            e.Renumbered = [];
            return made;
        }
    }

    public static IReadOnlyList<Note> All() => Get().Notes;

    /// <summary>
    /// One item's notes. When all the notes are held and nothing changed without naming its item they are picked from there (looking again only at the folders written to); otherwise only this item's notes/ folder is read (the other items' are not opened), into the bodies cache
    /// (the items used last), and read again only when its files' names, sizes or modified times differ. A page of one item does not settle id collisions across all the notes: <see cref="Get()"/> does, when all notes are asked for.
    /// </summary>
    public static IReadOnlyList<Note> ForWorkItem(int workItemId)
    {
        State s = Caches.S.Notes;
        string root = OpenSpecStoreApi.RepoRoot ?? "";
        long generation = OpenSpecStoreApi.RepoGeneration;
        bool warm;
        lock (s.Gate) warm = s.Entries.TryGetValue(root, out Entry? e) && e.Generation == generation && e.Read is not null && e.Outside == OpenSpecStoreApi.OutsideVersion;
        if (warm) return Get().Notes.Where(n => n.WorkItemId == workItemId).ToList();   // all notes are warm and nothing moved unnamed: assembling again looks only at the folders written to
        if (ItemsCache.Get(workItemId) is not WorkItems.PublicDTOs.WorkItem item) return [];
        string dir = Path.Combine(item.Path, "notes");
        DirectoryInfo info = new(dir);
        if (!info.Exists) return [];
        List<FileInfo> files = [.. info.GetFiles("n*.md").OrderBy(f => f.FullName, StringComparer.CurrentCulture)];
        string stamp = dir + "|" + string.Join('\n', files.Select(f => $"{f.Name}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"));
        long newest = files.Select(f => f.LastWriteTimeUtc.Ticks).DefaultIfEmpty(0).Max();
        ItemBodies bodies = Caches.S.Bodies.Of(root, workItemId);
        if (bodies.Notes is { } held && held.Holds(generation, stamp)) return held.Value.Parsed;
        DateTime readAt = Caches.S.Items.Clock();   // taken before reading, so a write during the read shows as racy
        NotesFolderRead folder = ReadFolder(dir, workItemId);
        bodies.Notes = new(generation, stamp, newest, readAt, folder);
        return folder.Parsed;
    }

    public static Note? Get(int id) => Get().Notes.FirstOrDefault(n => n.Id == id);

    /// <summary>Forgets what was read of a repository (all of them without a root): the next read goes to disk.</summary>
    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.Notes;
        lock (s.Gate) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(root); }
    }
}

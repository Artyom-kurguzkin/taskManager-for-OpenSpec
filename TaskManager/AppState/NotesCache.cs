using Notes;
using Notes.PublicDTOs;
using OpenSpecStore;

namespace TaskManager.AppState;

/// <summary>
/// The notes of each open repository, assembled once per OpenSpecStore content version. Each change's notes/ folder is read only when its files changed
/// (name, size, modified time) or the app wrote there itself; the other folders keep their parsed notes and objects. The module (Notes) reads a folder and settles
/// the id collisions; what is kept, and until when, is decided here.
/// </summary>
public static class NotesCache
{
    internal sealed class Entry
    {
        internal long Generation;
        internal long Version = -1;
        internal long Cursor;
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
    }

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
            if (e.Read is not null && e.Version == version) return e.Read;

            (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreApi.ChangesSince(e.Cursor);   // asked before reading: a write that lands meanwhile shows next time
            Dictionary<string, (string Fingerprint, NotesFolderRead Folder)> now = new(StringComparer.OrdinalIgnoreCase);
            List<NotesFolderRead> folders = [];
            foreach (WorkItems.PublicDTOs.WorkItem wi in ItemsCache.All(includeHidden: true))
            {
                string dir = Path.Combine(wi.Path, "notes");
                DirectoryInfo info = new(dir);
                if (!info.Exists) continue;
                string fingerprint = string.Join('\n', info.GetFiles("n*.md").OrderBy(f => f.FullName, StringComparer.CurrentCulture).Select(f => $"{f.Name}|{f.Length}|{f.LastWriteTimeUtc.Ticks}"));   // taken before reading, so a write during the read shows as a change next time
                NotesFolderRead folder;
                if (e.Folders.TryGetValue(dir, out var old) && old.Fingerprint == fingerprint && !ItemsCache.Written(dir, touched))
                {
                    folder = old.Folder;
                    NotesApi.Rebind(folder, wi.Id);   // a provisional number moved: no file to read again, only the owner of the notes changes
                }
                else folder = NotesApi.ReadFolder(dir, wi.Id);
                now[dir] = (fingerprint, folder);
                folders.Add(folder);
            }
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

    public static IReadOnlyList<Note> ForWorkItem(int workItemId) => Get().Notes.Where(n => n.WorkItemId == workItemId).ToList();

    public static Note? Get(int id) => Get().Notes.FirstOrDefault(n => n.Id == id);

    /// <summary>Forgets what was read of a repository (all of them without a root): the next read goes to disk.</summary>
    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.Notes;
        lock (s.Gate) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(root); }
    }
}

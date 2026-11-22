using OpenSpecStore;
using TaskManager.Helpers;

namespace TaskManager.AppState;

public static partial class ItemsCache
{
    /// <summary>How many times the board's folders were all looked at (a walk of the changes folder and a fingerprint of each folder); a write that patches its own item adds none (a test counts them).</summary>
    public static int Passes => Caches.S.Items.Passes;

    /// <summary>How many times the board was brought up to date from what the app wrote, in the folders it wrote in only (a test counts them).</summary>
    public static int Patches => Caches.S.Items.Patches;

    /// <summary>
    /// Write-through: when nothing outside the app changed files since the board was read (the store's outside count is the same) and everything the app touched since is a file it wrote and
    /// still holds the text of, inside a change folder the board already has, only those folders are read again; the store hands their parse the written text, so the written files are not
    /// opened. The others keep their folder objects, and so their items. Null when anything else happened (a folder deleted, moved or created, the config, an outside edit, a CLI's work):
    /// the caller then looks at every folder, as it always did.
    /// </summary>
    private static List<Folder>? Patched(State s, Entry e, string root, IReadOnlyList<string> touched, long outside)
    {
        if (e.Board is null || e.Outside != outside || touched.Count == 0) return null;
        string repository = Path.Combine(root, "openspec") + Path.DirectorySeparatorChar;
        HashSet<string> dirs = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in touched.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!path.StartsWith(repository, StringComparison.OrdinalIgnoreCase)) continue;   // a file of another open repository: not this board's
            if (!OpenSpecStoreApi.HoldsWritten(path)) return null;                           // deleted, moved, run by a CLI, or changed since it was written: only a pass can tell
            if (e.Folders.FirstOrDefault(f => Written(f.Dir, [path])) is not Folder folder) return null;   // a folder the board does not have, or a file that is no item's (config.yaml)
            dirs.Add(folder.Dir);
        }
        if (dirs.Count == 0) return null;
        List<Folder> now = [];
        foreach (Folder old in e.Folders)
        {
            if (!dirs.Contains(old.Dir)) { now.Add(old); continue; }
            try { if (Fresh(s, old.Dir, old.Archived, old, touched) is Folder read) now.Add(read); else return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
        s.Patches++;
        return now;
    }
}

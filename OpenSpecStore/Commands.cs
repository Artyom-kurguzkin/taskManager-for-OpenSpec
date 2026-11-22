namespace OpenSpecStore;

internal static class Commands
{
    internal const string StaleMessage = "This file changed on disk; your edit was not saved";
    private static long opened;
    internal static long NextOpened() => Interlocked.Increment(ref opened);

    /// <summary>Opens a repository and makes it the one the module Apis read and write. Unless asked to keep the others, every
    /// other open repository is closed (the single-repository case, and what tests do).</summary>
    internal static (bool Success, string Message) Open(string repoRoot, bool keepOthers)
    {
        string root = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(root)) return (false, $"There is no folder {root}");
        string openSpec = Path.Combine(root, "openspec");
        string setUp = "";
        if (!Directory.Exists(openSpec))   // a repository that has not used OpenSpec yet: the app lays out what the CLI's init would, minus its prompts
        {
            Directory.CreateDirectory(Path.Combine(openSpec, "specs"));
            Directory.CreateDirectory(Path.Combine(openSpec, "changes", "archive"));
            File.WriteAllText(Queries.ConfigFile(root), $"schema: spec-driven\n\n# What this repository is, for the agents that work in it (see openspec/REPO-TASKS.md).\ncontext: |\n  {Path.GetFileName(root)}.\n");
            setUp = $"Set up openspec/ in {Path.GetFileName(root)}: config.yaml, specs/ and changes/archive/. Describe the project in config.yaml's context.";
        }
        lock (OpenSpecStoreModule.Repos)
        {
            if (!keepOthers) Keep([root]);
            if (!OpenSpecStoreModule.Repos.TryGetValue(root, out Repo? repo))
            {
                repo = new Repo(root, OpenSpecStoreModule.State) { Opened = NextOpened() };
                OpenSpecStoreModule.Repos[root] = repo;
                OpenSpecStoreModule.State.Guests.Remove(root);   // a repository that was only a call's guest is now open: from here on its version and watcher are the open one's
                OpenSpecStoreModule.StartWatching(repo);
            }
            OpenSpecStoreModule.Current = repo;
        }
        return (true, setUp);
    }

    /// <summary>Makes an already open repository the current one; false when it is not open.</summary>
    internal static bool Use(string repoRoot)
    {
        lock (OpenSpecStoreModule.Repos)
        {
            if (!OpenSpecStoreModule.Repos.TryGetValue(Path.GetFullPath(repoRoot), out Repo? repo)) return false;
            OpenSpecStoreModule.Current = repo;
            return true;
        }
    }

    /// <summary>Closes every open repository not in the list (tabs that were closed).</summary>
    internal static void Keep(IEnumerable<string> roots)
    {
        HashSet<string> keep = new(roots.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        lock (OpenSpecStoreModule.Repos)
        {
            foreach (Repo repo in OpenSpecStoreModule.Repos.Values.Where(r => !keep.Contains(r.Root)).ToList())
            {
                OpenSpecStoreModule.StopWatching(repo);
                OpenSpecStoreModule.Repos.Remove(repo.Root);
                if (ReferenceEquals(OpenSpecStoreModule.State.Current, repo)) OpenSpecStoreModule.Current = null;
            }
        }
    }

    internal static void Close()
    {
        Keep([]);
        lock (OpenSpecStoreModule.Repos) OpenSpecStoreModule.State.Guests.Clear();
        OpenSpecStoreModule.LastSeen.Clear();
    }

    /// <summary>Creates a folder and the missing ones above it, each marked as the app's own: the watcher reports every folder that appears, and an unmarked one reloads every open page 300 ms after the save.</summary>
    internal static void CreateOwnedDirectory(string dir)
    {
        for (string? d = Path.GetFullPath(dir); d is not null && !Directory.Exists(d); d = Path.GetDirectoryName(d)) OpenSpecStoreModule.Own(d);
        Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Moves the temp file over the target. A virus scanner, a search indexer or a sync client opens a file the moment it is written and holds it
    /// for a few milliseconds, which makes the move fail with "access denied" or "in use": it is tried again for up to about a second, as the
    /// board sync's own writes are (BoardSync's LocalTree.WriteAtomic); a file that stays locked throws as before.
    /// </summary>
    private static void MoveOver(string tmp, string full)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, full, overwrite: true); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 40 && File.Exists(tmp) && !Directory.Exists(full)) { Thread.Sleep(25); }
        }
    }

    /// <summary>A temp name beside the file that no other writer uses: <c>&lt;file&gt;.&lt;8 hex&gt;.tmp</c> (the sync's scan ignores *.tmp).</summary>
    internal static string TempName(string full) => $"{full}.{Guid.NewGuid().ToString("N")[..8]}.tmp";

    /// <summary>Removes the temp files a crashed write left beside the file (its old fixed name and the unique ones); called under the folder's lock, so none of them is another writer's in-flight one.</summary>
    private static void RemoveLeftoverTemps(string full)
    {
        string dir = Path.GetDirectoryName(full)!, name = Path.GetFileName(full);
        if (!Directory.Exists(dir)) return;
        foreach (string temp in Directory.EnumerateFiles(dir, name + "*.tmp"))
        {
            string tail = Path.GetFileName(temp)[name.Length..];   // ".tmp" or ".<8 hex>.tmp"
            if (tail != ".tmp" && !(tail.Length == 13 && tail[0] == '.' && tail[1..9].All(Uri.IsHexDigit))) continue;
            OpenSpecStoreModule.Own(temp);
            try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // still open somewhere: the next write tries again
        }
    }

    // One writer per file in this process: a write queued behind another of the same file waits for it, so its stale check sees what that write left in LastSeen, never half of it.
    // The wait (like the folder lock's and MoveOver's) blocks only the thread that writes: the pages run their commands on the thread pool.
    // ponytail: one semaphore per path ever written, kept for the process (a few hundred bytes each); evict when a session writes more files than that is worth.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> PathGates = new(StringComparer.OrdinalIgnoreCase);

    internal static SemaphoreSlim GateOf(string full) => PathGates.GetOrAdd(full, _ => new SemaphoreSlim(1, 1));

    internal static IDisposable HoldPath(string full)
    {
        SemaphoreSlim gate = GateOf(full);
        gate.Wait();
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? gate = gate;
        public void Dispose() => Interlocked.Exchange(ref gate, null)?.Release();
    }

    /// <summary>Writes the file if what the app last read of it is still on disk. The change folder is locked from the check to the move, so another process cannot slip a write in between; a lock that is not released in time is a refusal with its reason.</summary>
    internal static (bool Success, string Message) WriteText(string path, string content)
    {
        string full = Path.GetFullPath(path);
        try
        {
            using (HoldPath(full))
            using (FolderLock.ForPath(full)) return WriteLocked(full, content);
        }
        catch (TimeoutException e) { return (false, e.Message); }
    }

    /// <summary>
    /// Reads the file as it is now (under the folder's lock), lets <paramref name="edit"/> change it and writes the result: no stale refusal, because the
    /// view is fresh. For files that are only ever appended to (events.log), where a refused write would lose the event.
    /// </summary>
    internal static (bool Success, string Message) UpdateText(string path, Func<string, string> edit)
    {
        string full = Path.GetFullPath(path);
        try
        {
            using (HoldPath(full))
            using (FolderLock.ForPath(full)) return WriteLocked(full, edit(Queries.ReadTextHeld(full)));
        }
        catch (TimeoutException e) { return (false, e.Message); }
    }

    private static (bool Success, string Message) WriteLocked(string full, string content)
    {
        string? onDisk = Queries.ReadShared(full);
        // Only files the app has read are guarded. A file that has since been moved or deleted (an archived change folder
        // whose name a new change reuses) is not a conflict: writing there overwrites nothing.
        if (OpenSpecStoreModule.LastSeen.TryGetValue(full, out string? seen) && onDisk is not null && seen != onDisk) return (false, StaleMessage);
        CreateOwnedDirectory(Path.GetDirectoryName(full)!);
        OpenSpecStoreModule.Own(full);
        RemoveLeftoverTemps(full);
        string tmp = TempName(full);   // write beside, then swap in: a reader or a crash never sees half a file
        OpenSpecStoreModule.Own(tmp);   // the watcher would report the temp file otherwise
        try
        {
            File.WriteAllText(tmp, content);
            MoveOver(tmp, full);
        }
        catch { try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } throw; }
        OpenSpecStoreModule.LastSeen[full] = content;
        OpenSpecStoreModule.Remember(full, content);   // what was written and the stamp it left: readers and the watcher's echo go by it (a write that failed above never gets here)
        OpenSpecStoreModule.Touch(full);   // this write's one version bump, after it, so a cache rebuilt meanwhile still counts as stale
        return (true, "");
    }

    internal static (bool Success, string Message) WriteRtmBlock(string yamlPath, string blockYaml)
    {
        string full = Path.GetFullPath(yamlPath);
        string current = OpenSpecStoreModule.LastSeen.TryGetValue(full, out string? seen) ? seen ?? "" : Queries.ReadText(full);
        return WriteText(full, RtmBlock.Replace(current, blockYaml));
    }

    internal static (bool Success, string Message) WriteRtmSection(string yamlPath, string key, string? sectionYaml)
    {
        string full = Path.GetFullPath(yamlPath);
        string current = OpenSpecStoreModule.LastSeen.TryGetValue(full, out string? seen) ? seen ?? "" : Queries.ReadText(full);
        string block = RtmBlock.Replace(RtmBlock.Extract(current) ?? "", key, sectionYaml);
        return WriteText(full, RtmBlock.Replace(current, RtmBlock.Root, block.Trim().Length == 0 ? null : block));
    }

    /// <summary>Replaces one section of the project's config.yaml with <paramref name="value"/> (lists as [a, b] when asked); null removes it. Everything else in the file stays as it was.</summary>
    internal static (bool Success, string Message) WriteConfigSection<T>(string key, T? value, bool flowLists, string? repoRoot)
    {
        if (Queries.ConfigFileOf(repoRoot) is not string file) return (false, "No repository is open");
        return WriteRtmSection(file, key, value is null ? null : YamlText.Writer(model: true, flowLists).Serialize(value));
    }

    /// <summary>
    /// Sets one plain value of the project's config.yaml on a single <c>key: value</c> line in its x-rtm block; null removes the key. Nothing else in the file changes. The file is read from disk
    /// first, so a pull since the store last looked is not mistaken for an outside edit during the write.
    /// </summary>
    internal static (bool Success, string Message) WriteConfigValue(string key, string? value, string? repoRoot)
    {
        if (Queries.ConfigFileOf(repoRoot) is not string file) return (false, "No repository is open");
        string full = Path.GetFullPath(file);
        string current = Queries.ReadText(full);
        string? line = value is null ? null : YamlText.Writer(model: false, flowLists: false).Serialize(new Dictionary<string, string> { [key] = value }).Replace("\r\n", "\n").TrimEnd('\n');
        string block = RtmBlock.ReplaceInline(RtmBlock.Extract(current) ?? "", key, line);
        return WriteText(full, RtmBlock.Replace(current, RtmBlock.Root, block.Trim().Length == 0 ? null : block));
    }

    internal static (bool Success, string Message) DeleteFolder(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) return (true, "");
        try
        {
            using (FolderLock.ForPath(full))
            {
                OpenSpecStoreModule.Own(full);
                foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)) OpenSpecStoreModule.Own(entry);   // the watcher reports each file inside
                Directory.Delete(full, recursive: true);
            }
        }
        catch (TimeoutException e) { return (false, e.Message); }
        OpenSpecStoreModule.DropWritten(full);
        OpenSpecStoreModule.Touch(full);
        return (true, "");
    }

    internal static (bool Success, string Message) DeleteFile(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) return (true, "");
        OpenSpecStoreModule.Own(full);
        File.Delete(full);
        OpenSpecStoreModule.LastSeen.TryRemove(full, out _);
        OpenSpecStoreModule.DropWritten(full);
        OpenSpecStoreModule.Touch(full);
        return (true, "");
    }

    internal static (bool Success, string Message) MoveFolder(string from, string to)
    {
        string src = Path.GetFullPath(from), dst = Path.GetFullPath(to);
        if (!Directory.Exists(src)) return (false, "That folder no longer exists");
        if (Directory.Exists(dst)) return (false, $"{Path.GetFileName(dst)} already exists");
        OpenSpecStoreModule.Own(src);
        foreach (string entry in Directory.EnumerateFileSystemEntries(src, "*", SearchOption.AllDirectories)) OpenSpecStoreModule.Own(entry);
        CreateOwnedDirectory(Path.GetDirectoryName(dst)!);
        Directory.Move(src, dst);
        OpenSpecStoreModule.Own(dst);
        foreach (string entry in Directory.EnumerateFileSystemEntries(dst, "*", SearchOption.AllDirectories)) OpenSpecStoreModule.Own(entry);
        OpenSpecStoreModule.DropWritten(src);
        OpenSpecStoreModule.DropWritten(dst);
        OpenSpecStoreModule.Touch(src, dst);
        return (true, "");
    }
}

namespace OpenSpecStore;

internal static class Commands
{
    internal const string StaleMessage = "This file changed on disk; your edit was not saved";
    private static long opened;

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
            File.WriteAllText(Path.Combine(openSpec, "config.yaml"), $"schema: spec-driven\n\n# What this repository is, for the agents that work in it (see openspec/REPO-TASKS.md).\ncontext: |\n  {Path.GetFileName(root)}.\n");
            setUp = $"Set up openspec/ in {Path.GetFileName(root)}: config.yaml, specs/ and changes/archive/. Describe the project in config.yaml's context.";
        }
        lock (OpenSpecStoreModule.Repos)
        {
            if (!keepOthers) Keep([root]);
            if (!OpenSpecStoreModule.Repos.TryGetValue(root, out Repo? repo))
            {
                repo = new Repo(root) { Opened = ++opened };
                OpenSpecStoreModule.Repos[root] = repo;
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
                if (ReferenceEquals(OpenSpecStoreModule.Current, repo)) OpenSpecStoreModule.Current = null;
            }
        }
    }

    internal static void Close()
    {
        Keep([]);
        OpenSpecStoreModule.LastSeen.Clear();
    }

    internal static (bool Success, string Message) WriteText(string path, string content)
    {
        string full = Path.GetFullPath(path);
        string? onDisk = File.Exists(full) ? File.ReadAllText(full) : null;
        // Only files the app has read are guarded. A file that has since been moved or deleted (an archived change folder
        // whose name a new change reuses) is not a conflict: writing there overwrites nothing.
        if (OpenSpecStoreModule.LastSeen.TryGetValue(full, out string? seen) && onDisk is not null && seen != onDisk) return (false, StaleMessage);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        OpenSpecStoreModule.MarkOwn(full);
        string tmp = full + ".tmp";   // write beside, then swap in: a reader or a crash never sees half a file
        OpenSpecStoreModule.MarkOwn(tmp);   // the watcher would report the temp file otherwise
        File.WriteAllText(tmp, content);
        File.Move(tmp, full, overwrite: true);
        OpenSpecStoreModule.LastSeen[full] = content;
        OpenSpecStoreModule.Bump();   // after the write, so a cache rebuilt meanwhile still counts as stale
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

    internal static (bool Success, string Message) DeleteFolder(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) return (true, "");
        OpenSpecStoreModule.MarkOwn(full);
        foreach (string entry in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)) OpenSpecStoreModule.MarkOwn(entry);   // the watcher reports each file inside
        Directory.Delete(full, recursive: true);
        OpenSpecStoreModule.Bump();
        return (true, "");
    }

    internal static (bool Success, string Message) MoveFolder(string from, string to)
    {
        string src = Path.GetFullPath(from), dst = Path.GetFullPath(to);
        if (!Directory.Exists(src)) return (false, "That folder no longer exists");
        if (Directory.Exists(dst)) return (false, $"{Path.GetFileName(dst)} already exists");
        OpenSpecStoreModule.MarkOwn(src);
        foreach (string entry in Directory.EnumerateFileSystemEntries(src, "*", SearchOption.AllDirectories)) OpenSpecStoreModule.MarkOwn(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        Directory.Move(src, dst);
        OpenSpecStoreModule.MarkOwn(dst);
        foreach (string entry in Directory.EnumerateFileSystemEntries(dst, "*", SearchOption.AllDirectories)) OpenSpecStoreModule.MarkOwn(entry);
        OpenSpecStoreModule.Bump();
        return (true, "");
    }
}

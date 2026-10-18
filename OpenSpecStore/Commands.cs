namespace OpenSpecStore;

internal static class Commands
{
    internal const string StaleMessage = "This file changed on disk; your edit was not saved";

    internal static (bool Success, string Message) Open(string repoRoot)
    {
        string root = Path.GetFullPath(repoRoot);
        string openSpec = Path.Combine(root, "openspec");
        if (!Directory.Exists(openSpec)) return (false, "No openspec/ folder here. Run `openspec init` in this repo first.");
        OpenSpecStoreModule.RepoRoot = root;
        OpenSpecStoreModule.LastSeen.Clear();
        OpenSpecStoreModule.Bump();
        OpenSpecStoreModule.StartWatching(openSpec);
        return (true, "");
    }

    internal static void Close()
    {
        OpenSpecStoreModule.StopWatching();
        OpenSpecStoreModule.RepoRoot = null;
        OpenSpecStoreModule.LastSeen.Clear();
        OpenSpecStoreModule.Bump();
    }

    internal static (bool Success, string Message) WriteText(string path, string content)
    {
        string full = Path.GetFullPath(path);
        string? onDisk = File.Exists(full) ? File.ReadAllText(full) : null;
        // Only files the app has read are guarded; a brand-new file has nothing to conflict with.
        if (OpenSpecStoreModule.LastSeen.TryGetValue(full, out string? seen) && seen != onDisk) return (false, StaleMessage);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        OpenSpecStoreModule.MarkOwn(full);
        File.WriteAllText(full, content);
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
        Directory.Delete(full, recursive: true);
        OpenSpecStoreModule.Bump();
        return (true, "");
    }
}

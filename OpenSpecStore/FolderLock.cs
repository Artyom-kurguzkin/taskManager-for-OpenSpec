namespace OpenSpecStore;

/// <summary>
/// A cross-process lock per change folder, so the desktop app, the headless board server and agents in worktrees never interleave two writes
/// of the same folder (check, temp write, move). It is a file under the git common dir (<c>rtm/locks/&lt;folder&gt;.lock</c>) opened with
/// FileShare.None, the same kind of lock the id allocator uses: every worktree of a repository shares it, and it dies with its process.
/// </summary>
internal static class FolderLock
{
    /// <summary>A test seam: how long to wait for a held lock, for the code running from where it is set; null = the usual 5 s.</summary>
    internal static readonly AsyncLocal<TimeSpan?> WaitLimit = new();

    internal static string PathOf(string repoRoot, string folder)
    {
        string name = string.Concat(folder.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(IdAllocator.CommonDir(Path.GetFullPath(repoRoot)), "rtm", "locks", name + ".lock");
    }

    /// <summary>Takes the folder's lock, waiting for its holder up to the wait limit; throws a TimeoutException saying so after. Dispose it to release.</summary>
    internal static IDisposable Hold(string repoRoot, string folder)
    {
        string path = PathOf(repoRoot, folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return IdAllocator.Lock(path, $"the write lock of {folder}", WaitLimit.Value);
    }

    /// <summary>
    /// The lock that guards a path under <c>&lt;repo&gt;/openspec</c>: the change folder it is in (an archived change has its own, so a change and
    /// its later archived copy do not block each other), else one for everything else in openspec/. Null for a path outside any openspec folder.
    /// </summary>
    internal static IDisposable? ForPath(string fullPath)
    {
        string[] parts = Path.GetFullPath(fullPath).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int at = Array.FindLastIndex(parts, p => p.Equals("openspec", StringComparison.OrdinalIgnoreCase));
        if (at <= 0) return null;
        string root = string.Join(Path.DirectorySeparatorChar, parts[..at]);
        if (at == 1) root += Path.DirectorySeparatorChar;   // "C:" alone is the drive's current folder, "" is the root
        string[] rest = parts[(at + 1)..];
        string folder = rest.Length >= 2 && rest[0] == "changes"
            ? (rest[1] == "archive" && rest.Length >= 3 ? "archive~" + rest[2] : rest[1])
            : "_openspec";
        return Hold(root, folder);
    }
}

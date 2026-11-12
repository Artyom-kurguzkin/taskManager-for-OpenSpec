using BoardSync.Storage;

namespace BoardSync.Sync;

/// <summary>
/// Step 0 of the loop: while git is in the middle of an operation, or a board file still has conflict markers, the files are half something else's,
/// so a sync would spread a half-done state. Reads only; never throws for a git folder that is not there.
/// </summary>
internal static class GitGuard
{
    private static readonly (string Entry, bool IsDirectory, string Operation)[] Operations =
    [
        ("MERGE_HEAD", false, "merge"),
        ("rebase-merge", true, "rebase"),
        ("rebase-apply", true, "rebase"),       // ponytail: git am also leaves this folder and is reported as a rebase.
        ("CHERRY_PICK_HEAD", false, "cherry-pick"),
        ("REVERT_HEAD", false, "revert"),
    ];

    /// <summary>
    /// null = not paused, else the message to show. <paramref name="synced"/> is what the last sync ended with: a file that is byte-identical to it was written by the sync itself
    /// (it may have come from the storage), so it is never looked at for markers; only what git or the user changed since is.
    /// </summary>
    internal static string? Pause(string localRoot, string? gitFolder, IReadOnlyDictionary<string, ManifestEntry>? synced = null, bool checkMarkers = true)
    {
        string? git = Resolve(gitFolder);
        if (git is null) return null;

        foreach ((string entry, bool isDirectory, string operation) in Operations)
        {
            string path = Path.Combine(git, entry);
            if (isDirectory ? Directory.Exists(path) : File.Exists(path))
                return $"Sync is paused while a git {operation} is in progress. Finish or abort it, then sync again.";
        }

        if (!checkMarkers) return null;       // a folder of whole files (attachments): text that looks like a conflict is just text
        List<string> conflicted = ConflictedFiles(localRoot, synced);
        if (conflicted.Count == 0) return null;
        return conflicted.Count == 1
            ? $"Sync is paused: {conflicted[0]} still has merge conflict markers."
            : $"Sync is paused: {conflicted[0]} and {conflicted.Count - 1} more still have merge conflict markers.";
    }

    /// <summary>The git directory itself, or the one a worktree's .git file ("gitdir: path", absolute or relative to the file) names; null when there is none.</summary>
    private static string? Resolve(string? gitFolder)
    {
        if (string.IsNullOrWhiteSpace(gitFolder)) return null;
        try
        {
            string full = Path.GetFullPath(gitFolder);
            if (Directory.Exists(full)) return full;
            if (!File.Exists(full)) return null;
            string? line = File.ReadLines(full).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
            if (line is null) return null;
            string target = line["gitdir:".Length..].Trim();
            string resolved = Path.GetFullPath(target, Path.GetDirectoryName(full)!);
            return Directory.Exists(resolved) ? resolved : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>Board files (relative paths, ordinal order) changed since the last sync with a "&lt;&lt;&lt;&lt;&lt;&lt;&lt; " line, later a "=======" line, later a "&gt;&gt;&gt;&gt;&gt;&gt;&gt; " line, outside fenced code blocks. Links are not followed.</summary>
    private static List<string> ConflictedFiles(string localRoot, IReadOnlyDictionary<string, ManifestEntry>? synced)
    {
        List<string> found = [];
        // ponytail: every changed file is read in full on each sync; a board is a few hundred small files. Skip by size when large files appear.
        IEnumerator<(string Full, string Relative)> files;
        try { files = LocalTree.Walk(localRoot, []).GetEnumerator(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return found; }
        while (true)
        {
            try { if (!files.MoveNext()) break; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { break; }   // unreadable: the sync itself reports it
            (string file, string relative) = files.Current;
            if (file.EndsWith(LocalTree.TempSuffix, StringComparison.Ordinal)) continue;
            try
            {
                if (new FileInfo(file).Length > StorageLimits.ObjectMax) continue;      // never synced, so never spread by the sync; and not read into memory
                byte[] bytes = File.ReadAllBytes(file);
                if (synced is not null && synced.TryGetValue(relative, out ManifestEntry? known) && !known.Deleted && known.Hash == SyncModels.HashOf(bytes)) continue;
                if (HasMarkers(System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))) found.Add(relative);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static bool HasMarkers(IEnumerable<string> lines)
    {
        int stage = 0;      // 0 looking for the opener, 1 for the divider, 2 for the closer
        string? fence = null;   // inside a fenced code block: what closes it
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            string trimmed = line.TrimStart(' ');
            if (line.Length - trimmed.Length <= 3 && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                string mark = trimmed[..3];
                if (fence is null) fence = mark; else if (fence == mark) fence = null;
                continue;
            }
            if (fence is not null) continue;
            if (stage == 0 && line.StartsWith("<<<<<<< ", StringComparison.Ordinal)) stage = 1;
            else if (stage == 1 && line.StartsWith("=======", StringComparison.Ordinal) && line.TrimEnd().Length == 7) stage = 2;
            else if (stage == 2 && line.StartsWith(">>>>>>> ", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}

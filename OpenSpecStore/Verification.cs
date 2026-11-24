namespace OpenSpecStore;

/// <summary>
/// "Is what the readers hold still what is on disk?", answered cheaply: one listing of the files the app reads (the change folders, the main specs and the project's config file)
/// with the size and modified time of each, compared to the stamps held from the last look. Only the files whose stamp differs are reported, as paths the readers read again
/// (the record the app's own writes use, see <see cref="OpenSpecStoreModule.TouchesSince"/>); no file is opened. A stamp taken within a few milliseconds of the look is "racy" (an edit after it can share
/// its size and time), so it is reported again at the next look, as git does. Stamps are <c>FileInfo.Length</c> and <c>LastWriteTimeUtc</c>: nothing platform-specific.
/// </summary>
internal static class Verification
{
    /// <summary>How close to the look a file's modified time may be and still be doubted: a file system stamps with a clock that ticks about every 16 ms.</summary>
    private static readonly long Slack = TimeSpan.FromMilliseconds(20).Ticks;

    /// <summary>
    /// Looks at the repository's files against the held stamps. With <paramref name="maxAge"/> it does nothing when the last look is younger than that (a tool call right after another).
    /// The first look has nothing to compare with: it makes every reader read again (as <see cref="OpenSpecStoreModule.BumpOutside"/>, and like it without raising <see cref="StoreState.Changed"/>: nothing can be told what changed,
    /// and the app takes it while loading, before the pages have read). A later look raises <see cref="StoreState.Changed"/> when a file differs.
    /// True when something may have changed.
    /// </summary>
    internal static bool Verify(Repo repo, TimeSpan maxAge)
    {
        bool changed, real, first;
        lock (repo.Verified)
        {
            VerifiedState v = repo.Verified;
            DateTime at = repo.Owner.Time.GetUtcNow().UtcDateTime;   // taken before the listing, so a write during it shows as racy
            if (v.Stamps is not null && maxAge > TimeSpan.Zero && at - v.At < maxAge) return false;
            Dictionary<string, (long Length, long Ticks)> now;
            try { now = List(repo); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }   // files moving under the look: the next one is the retry
            repo.Owner.Listings++;

            List<string> differ = [];
            real = false;
            first = v.Stamps is null;
            if (v.Stamps is not { } old) { real = true; Interlocked.Increment(ref repo.Outside); }
            else
            {
                foreach ((string path, (long Length, long Ticks) stamp) in now)
                {
                    bool known = old.TryGetValue(path, out (long Length, long Ticks) before);
                    if (known && before == stamp && !v.Racy.Contains(path)) continue;
                    differ.Add(path);
                    real |= !known || before != stamp;
                }
                foreach (string path in old.Keys) if (!now.ContainsKey(path)) { differ.Add(path); real = true; }
            }
            v.Stamps = now;
            v.Racy = [.. now.Where(p => p.Value.Ticks >= at.Ticks - Slack).Select(p => p.Key)];
            v.At = at;
            changed = real || differ.Count > 0;
            if (differ.Count > 0) OpenSpecStoreModule.Touched(repo, differ);
            if (changed) Interlocked.Increment(ref repo.Version);
        }
        if (real && !first) Raise(repo);
        return changed;
    }

    /// <summary>The app wrote or deleted these paths itself: their stamps are taken as they are now, so the next look does not report the app's own writes as outside edits.</summary>
    internal static void Adopt(Repo repo, IEnumerable<string> paths)
    {
        lock (repo.Verified)
        {
            Dictionary<string, (long Length, long Ticks)>? stamps = repo.Verified.Stamps;
            if (stamps is null) return;
            foreach (string path in paths)
            {
                if (!Watched(repo, path)) continue;
                string below = path + Path.DirectorySeparatorChar;
                foreach (string key in stamps.Keys.Where(k => k.Equals(path, StringComparison.OrdinalIgnoreCase) || k.StartsWith(below, StringComparison.OrdinalIgnoreCase)).ToList()) stamps.Remove(key);
                try
                {
                    if (File.Exists(path)) { FileInfo f = new(path); stamps[f.FullName] = (f.Length, f.LastWriteTimeUtc.Ticks); }
                    else if (Directory.Exists(path))
                        foreach (FileInfo f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)) stamps[f.FullName] = (f.Length, f.LastWriteTimeUtc.Ticks);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // moving under us: the next look reports it
            }
        }
    }

    /// <summary>Whether a path is one the look covers: under the change folders or the main specs, or the config file.</summary>
    private static bool Watched(Repo repo, string path)
    {
        string full = Path.GetFullPath(path);
        return full.Equals(Path.GetFullPath(Queries.ConfigFile(repo.Root)), StringComparison.OrdinalIgnoreCase)
            || Roots(repo).Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase) && (full.Length == r.Length || full[r.Length] is '\\' or '/'));
    }

    private static string[] Roots(Repo repo) => [Path.GetFullPath(Path.Combine(repo.OpenSpec, "changes")), Path.GetFullPath(Path.Combine(repo.OpenSpec, "specs"))];

    /// <summary>The one listing: every file under the change folders and the main specs, and the config file, with size and modified time.</summary>
    private static Dictionary<string, (long Length, long Ticks)> List(Repo repo)
    {
        Dictionary<string, (long Length, long Ticks)> stamps = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in Roots(repo))
            if (Directory.Exists(root))
                foreach (FileInfo f in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories)) stamps[f.FullName] = (f.Length, f.LastWriteTimeUtc.Ticks);
        FileInfo config = new(Queries.ConfigFile(repo.Root));
        if (config.Exists) stamps[config.FullName] = (config.Length, config.LastWriteTimeUtc.Ticks);
        return stamps;
    }

    private static void Raise(Repo repo)
    {
        Action? changed;
        lock (repo.Owner.Repos) changed = repo.Owner.Changed;
        // a listener that throws on a timer's thread ends the process; files still moving are the usual cause, and the next look raises again
        try { changed?.Invoke(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>What the last look at a repository saw: the stamp of each file, which of them were racy, and when.</summary>
internal sealed class VerifiedState
{
    public Dictionary<string, (long Length, long Ticks)>? Stamps;
    public HashSet<string> Racy = [];
    public DateTime At;
}

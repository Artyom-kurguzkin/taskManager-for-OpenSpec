namespace BoardSync.Sync;

/// <summary>What scan.json holds: per file the size, last-write ticks and hash the scan saw, when that scan began, and the files that were left for later because they were still being written.</summary>
internal sealed class StatCacheFile
{
    public long ScannedAt { get; set; }
    public Dictionary<string, StatEntry> Files { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> Settling { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class StatEntry
{
    public long Size { get; set; }
    public long Mtime { get; set; }
    public string Hash { get; set; } = "";
}

/// <summary>
/// One walk's memory of the files (git's index trick): a file whose size and last-write time are what the cache saw is not read again, its hash is the cached one.
/// The racy rule: a cache entry is only trusted when the file was last written more than two seconds before the scan that saved it began; a later edit of the same size
/// inside the same timestamp tick would otherwise be invisible. A missing or damaged cache trusts nothing. The scan, the git guard, the marker list and the status line all use it,
/// so a pass reads each changed file once and an unchanged one never. Bytes are never kept; only (size, time, hash) per path.
/// </summary>
internal sealed class StatCache
{
    /// <summary>A file written this recently before a scan began may be written again in the same timestamp tick, so its cached hash is not trusted.</summary>
    internal static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>A file that was left for later (still being written) is taken anyway after this long.</summary>
    internal static readonly TimeSpan MaxDeferral = TimeSpan.FromMinutes(5);

    private readonly SyncStateStore? store;
    private readonly StatCacheFile previous;
    private readonly DateTime now;
    private readonly Dictionary<string, StatEntry> next = new(StringComparer.Ordinal);
    private Dictionary<string, long> settling;
    private bool touched;
    private readonly object gate = new();

    private StatCache(SyncStateStore? store, StatCacheFile? previous, DateTime now)
    {
        this.store = store;
        this.previous = Sanitise(previous);
        this.now = now;
        settling = new Dictionary<string, long>(this.previous.Settling, StringComparer.Ordinal);
    }

    /// <summary>The cache saved in <paramref name="store"/>'s folder (none when the store is null, missing or damaged); <paramref name="now"/> is when this scan begins.</summary>
    internal static StatCache Open(SyncStateStore? store, DateTime now) => new(store, store?.LoadScan(), now);

    private static StatCacheFile Sanitise(StatCacheFile? file)
    {
        StatCacheFile clean = new() { ScannedAt = file?.ScannedAt ?? 0 };
        foreach ((string path, StatEntry? entry) in file?.Files ?? [])
            if (path is not null && entry is { Hash.Length: > 0 }) clean.Files[path] = entry;
        foreach ((string path, long first) in file?.Settling ?? [])
            if (path is not null) clean.Settling[path] = first;
        return clean;
    }

    /// <summary>The cached hash of the file when its size and last write are the ones the cache saw and the entry is not racy; null = read it.</summary>
    internal string? Lookup(string relative, long size, long mtimeTicks)
    {
        lock (gate)
        {
            if (next.TryGetValue(relative, out StatEntry? fresh) && fresh.Size == size && fresh.Mtime == mtimeTicks && mtimeTicks < now.Ticks - RacyWindow.Ticks) return fresh.Hash;    // taken by an earlier walk of this very pass
            if (previous.Files.TryGetValue(relative, out StatEntry? entry) && entry.Size == size && entry.Mtime == mtimeTicks && mtimeTicks < previous.ScannedAt - RacyWindow.Ticks) return entry.Hash;
            return null;
        }
    }

    /// <summary>Remembers what was seen of a file. The size and time must be the ones looked at BEFORE the file was read: an edit that lands in between then shows as a change next time.</summary>
    internal void Record(string relative, long size, long mtimeTicks, string hash)
    {
        lock (gate)
        {
            touched = true;
            next[relative] = new StatEntry { Size = size, Mtime = mtimeTicks, Hash = hash };
        }
    }

    /// <summary>
    /// Called by the scan for every file: true when the file was written less than <paramref name="window"/> before this scan began and has been put off for less than <see cref="MaxDeferral"/>,
    /// so it is left for the next pass. After <see cref="MaxDeferral"/> it is taken anyway and its clock starts again.
    /// </summary>
    internal bool Defer(string relative, DateTime writtenUtc, TimeSpan window)
    {
        lock (gate)
        {
            if (window <= TimeSpan.Zero || now - writtenUtc >= window) return false;
            long first = previous.Settling.TryGetValue(relative, out long since) ? since : now.Ticks;
            if (now.Ticks - first >= MaxDeferral.Ticks) return false;
            settling[relative] = first;
            return true;
        }
    }

    /// <summary>The scan is about to decide who settles: what an earlier scan put off and this one does not is forgotten.</summary>
    internal void BeginScan()
    {
        lock (gate)
        {
            touched = true;
            settling = [];
        }
    }

    /// <summary>Writes the cache when it differs from the one that was opened (an idle pass writes nothing). Never throws: a cache that cannot be written only costs the next scan a re-read.</summary>
    internal void Save()
    {
        if (store is null) return;
        lock (gate)
        {
            if (!touched) return;
            bool same = next.Count == previous.Files.Count && settling.Count == previous.Settling.Count
                && next.All(p => previous.Files.TryGetValue(p.Key, out StatEntry? old) && old.Size == p.Value.Size && old.Mtime == p.Value.Mtime && old.Hash == p.Value.Hash)
                && settling.All(p => previous.Settling.TryGetValue(p.Key, out long old) && old == p.Value);
            // An entry that was racy for the scan that saved it is read again by every scan until a later scan saves the cache: written once more, as git refreshes its index after a racy read.
            bool racyRead = now.Ticks > previous.ScannedAt && previous.Files.Any(p => p.Value.Mtime >= previous.ScannedAt - RacyWindow.Ticks && next.ContainsKey(p.Key));
            if (same && !racyRead) return;
            try { store.SaveScan(new StatCacheFile { ScannedAt = now.Ticks, Files = next, Settling = settling }); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}

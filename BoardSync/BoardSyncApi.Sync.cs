using BoardSync.PublicDTOs;
using BoardSync.Sync;

namespace BoardSync;

public static partial class BoardSyncApi
{
    /// <summary>
    /// Syncs <see cref="SyncOptions.LocalRoot"/> with the storage: uploads what changed here, downloads what changed there, merges what changed on both,
    /// safely against other computers doing the same at the same moment. One sync at a time per directory in this process; a second call waits.
    /// Writes into the directory are plain file writes (a watcher sees them as outside edits). Storage problems come back as <see cref="SyncOutcome.Failed"/>, never as an exception.
    /// </summary>
    public static Task<SyncReport> Sync(SyncOptions options, CancellationToken ct = default) => SyncEngine.Run(options, null, ct);

    /// <summary>
    /// The files under <paramref name="localRoot"/> that a sync run at <paramref name="utcNow"/> with this <paramref name="window"/> would leave settling: written less than the window ago (the scan's own rule),
    /// as paths relative to the root. A stat walk, nothing is read or written; links and the files a sync never takes (editor temp files) are not listed. A file dated after <paramref name="utcNow"/> (a skewed clock) is not
    /// listed: waiting for it would never end. Empty when the window is zero or the folder is missing.
    /// </summary>
    public static IReadOnlyList<string> RecentlyWritten(string localRoot, DateTime utcNow, TimeSpan window)
    {
        if (window <= TimeSpan.Zero) return [];
        List<string> recent = [];
        try
        {
            foreach ((string full, string relative, FileInfo info) in LocalTree.Walk(localRoot, []))
            {
                if (full.EndsWith(LocalTree.TempSuffix, StringComparison.Ordinal)) continue;
                TimeSpan age = utcNow - info.LastWriteTimeUtc;
                if (age >= TimeSpan.Zero && age < window) recent.Add(relative);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // a file vanished or is locked mid-walk: what was seen so far is the answer
        return [.. recent.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Why a sync would be paused right now, for the host's status display: git is in the middle of a merge, rebase, cherry-pick or revert, or a board file under
    /// <paramref name="localRoot"/> still has conflict markers. null = not paused (also when <paramref name="gitFolder"/> is null or does not exist). No storage is needed.
    /// <paramref name="gitFolder"/> is the git directory or the path of a worktree's .git file.
    /// <paramref name="stateFolder"/> is the sync's state folder (<see cref="SyncOptions.StateFolder"/>): with it, files that are still byte-identical to what the last sync wrote are not looked at,
    /// exactly as in a sync, so the status shown here and the sync agree. Markers inside fenced code blocks never count. It only reads: nothing is written, not even the stat cache.
    /// </summary>
    public static string? GitPause(string localRoot, string? gitFolder, string? stateFolder = null)
    {
        if (string.IsNullOrWhiteSpace(stateFolder)) return GitGuard.Pause(localRoot, gitFolder);
        SyncStateStore store = new(stateFolder);
        StatCache stat = StatCache.Open(store, DateTime.UtcNow);        // the cache a pass saved: files it saw unchanged are not read, only listed
        return GitGuard.Pause(localRoot, gitFolder, store.Load()?.Entries, cache: stat);   // a look, never a write: the cache is saved by the passes only, so showing the status touches no file
    }
}

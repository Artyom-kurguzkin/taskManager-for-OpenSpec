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

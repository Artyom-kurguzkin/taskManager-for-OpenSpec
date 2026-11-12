namespace BoardSync.PublicDTOs;

/// <summary>What one <see cref="BoardSyncApi.Sync"/> call needs.</summary>
/// <param name="LocalRoot">The directory to sync (the caller passes openspec/changes): every file under it, recursively.</param>
/// <param name="StateFolder">This computer's private sync state for this project, outside the repository; created when missing.</param>
/// <param name="Storage">Where the shared copy of the board is kept.</param>
/// <param name="DeviceId">Short stable id of this computer; part of every clock this computer issues.</param>
/// <param name="UtcNow">The clock; tests inject theirs.</param>
/// <param name="AllowMassDelete">Lets a sync delete more than the usual share of the board (the brake otherwise returns <see cref="SyncOutcome.Blocked"/>).</param>
/// <param name="EditedInApp">Paths (relative to LocalRoot, forward slashes) the app itself wrote since the last successful sync; null = treat every local change as an edit. A local change NOT in this set whose content is an earlier version of that path (the manifest history) came back through git and is stale: the storage wins.</param>
/// <param name="WholeFilesOnly">For a folder of binary or opaque files (attachments): the text merge is never used, whatever the file name (an event log or a markdown file included). A path changed on both sides goes whole to the newer side, the losing local bytes are kept in the trash and one <see cref="SyncConflict"/> is reported. The conflict-marker scan of the git guard is skipped; only a git operation in progress under <paramref name="GitFolder"/> can still pause the sync. Everything else (deletions and tombstones, the 24-hour brake, the trash, links, first connection) is unchanged.</param>
/// <param name="GitFolder">The repository git directory (.git); when a merge, rebase or cherry-pick is in progress there, or a file under LocalRoot holds conflict markers, the sync is Blocked and writes nothing. null = no git check.</param>
public sealed record SyncOptions(
    string LocalRoot,
    string StateFolder,
    IBoardStorage Storage,
    string DeviceId,
    Func<DateTime>? UtcNow = null,
    bool AllowMassDelete = false,
    IReadOnlySet<string>? EditedInApp = null,
    string? GitFolder = null,
    bool WholeFilesOnly = false);

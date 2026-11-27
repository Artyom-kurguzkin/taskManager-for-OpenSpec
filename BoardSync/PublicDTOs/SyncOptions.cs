namespace BoardSync.PublicDTOs;

/// <summary>What one <see cref="BoardSyncApi.Sync"/> call needs.</summary>
/// <param name="LocalRoot">The directory to sync (the caller passes openspec/changes): every file under it, recursively.</param>
/// <param name="StateFolder">This computer's private sync state for this project, outside the repository; created when missing.</param>
/// <param name="Storage">Where the shared copy of the board is kept.</param>
/// <param name="DeviceId">Short stable id of this computer; part of every clock this computer issues.</param>
/// <param name="UtcNow">The clock; tests inject theirs.</param>
/// <param name="EditedInApp">Paths (relative to LocalRoot, forward slashes) the app itself wrote since the last successful sync; null = treat every local change as an edit. A local change NOT in this set whose content is an earlier version of that path (the manifest history) came back through git and is stale: the storage wins.</param>
/// <param name="WholeFilesOnly">For a folder of binary or opaque files (attachments): the text merge is never used, whatever the file name (an event log or a markdown file included). A path changed on both sides goes whole to the newer side, the losing local bytes are kept in the trash and one <see cref="SyncConflict"/> is reported. The conflict-marker scan of the git guard is skipped; only a git operation in progress under <paramref name="GitFolder"/> can still pause the sync. Everything else (deletions and tombstones, the trash, links, first connection) is unchanged.</param>
/// <param name="GitFolder">The repository git directory (.git); when a merge, rebase or cherry-pick is in progress there, or a file under LocalRoot holds conflict markers, the sync is Blocked and writes nothing. null = no git check.</param>
/// <param name="SettleWindow">A file whose last write is less than this long before the scan is being written right now (an agent, an editor): it is left for the next pass, like a link or an oversized file (nothing is uploaded, downloaded over it or deleted for it) and is listed in <see cref="SyncReport.Settling"/>. A file put off for 5 minutes is taken anyway. <see cref="TimeSpan.Zero"/> (the default of the record) = off; a host passes <see cref="DefaultSettleWindow"/>.</param>
/// <param name="ProjectId">The project this board belongs to: the hash of the repository's first commit (the same on every clone); null = no git, no check. A storage list that names another project stops the sync with <see cref="SyncBlock.OtherProject"/> before anything is changed; a list without a name is given this one.</param>
/// <param name="CommittedContent">The text of a board file (path relative to LocalRoot) as the last commit has it, or null; the base of the three-way merge for a file the sync has no saved base for (a first connection, or a lost base copy). Read only; nothing is written to git.</param>
/// <param name="RemovedOnPurpose">Only with <paramref name="WholeFilesOnly"/>: whether the file at a path (relative to LocalRoot) was removed on purpose, which the host knows from the items' event logs (attachments live outside the board folder). A file that is missing on one side and not removed on purpose is put back from the other. null = every removal counts as one (plain deletions).</param>
public sealed record SyncOptions(
    string LocalRoot,
    string StateFolder,
    IBoardStorage Storage,
    string DeviceId,
    Func<DateTime>? UtcNow = null,
    IReadOnlySet<string>? EditedInApp = null,
    string? GitFolder = null,
    bool WholeFilesOnly = false,
    TimeSpan SettleWindow = default,
    string? ProjectId = null,
    Func<string, string?>? CommittedContent = null,
    Func<string, bool>? RemovedOnPurpose = null)
{
    /// <summary>The settle window a host passes: ten seconds.</summary>
    public static readonly TimeSpan DefaultSettleWindow = TimeSpan.FromSeconds(10);
}

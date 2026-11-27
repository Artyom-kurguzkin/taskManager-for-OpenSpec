namespace BoardSync.PublicDTOs;

/// <summary>What one sync did. Paths are relative to the synced directory, with forward slashes; <paramref name="BackedUp"/> holds the full paths of the backup copies.</summary>
public sealed record SyncReport(
    SyncOutcome Outcome,
    string? Message,
    IReadOnlyList<string> Uploaded,
    IReadOnlyList<string> Downloaded,
    IReadOnlyList<string> Merged,
    IReadOnlyList<string> DeletedHere,
    IReadOnlyList<string> DeletedInStorage,
    IReadOnlyList<SyncConflict> Conflicts,
    IReadOnlyList<string> BackedUp,
    IReadOnlyList<string>? stale = null,
    IReadOnlyList<string>? trashed = null,
    SyncBlock blocked = SyncBlock.None,
    string? trashFolder = null,
    string? backupFolder = null,
    IReadOnlyList<string>? replaced = null,
    IReadOnlyList<string>? skippedLinks = null,
    IReadOnlyList<string>? tooLarge = null,
    IReadOnlyList<string>? settling = null)
{
    /// <summary>What the sync did on its own that an item should say: folders and files put back, folders followed to where another computer moved them, edits carried into an archived or merged item. Nothing here was deleted.</summary>
    public IReadOnlyList<SyncNote> Notes { get; init; } = [];

    /// <summary>Paths of files written so recently (<see cref="SyncOptions.SettleWindow"/>) that this sync left them for the next one, like links: nothing was uploaded, overwritten or deleted for them. A file put off for 5 minutes is taken anyway.</summary>
    public IReadOnlyList<string> Settling { get; init; } = settling ?? [];

    /// <summary>Change folders (relative to the synced directory) held back whole this pass because a file in them is still settling (see <see cref="Settling"/>): none of their files was uploaded, downloaded or deleted; the next pass takes them once nothing in them is being written.</summary>
    public IReadOnlyList<string> WaitingFolders { get; init; } = [];

    /// <summary>Change folders in the storage that are incomplete (no live .openspec.yaml, or a merged item whose sources are still live there) and not yet on this computer: not downloaded and not counted; the computer that made them uploads the rest, and they flow normally after.</summary>
    public IReadOnlyList<string> IncompleteFolders { get; init; } = [];

    /// <summary>Why the sync was <see cref="SyncOutcome.Blocked"/> (<see cref="SyncBlock.None"/> for every other outcome).</summary>
    public SyncBlock Blocked { get; init; } = blocked;

    /// <summary>The folder of this sync's trash stamp (the copies in <see cref="Trashed"/> are inside it); null when the sync trashed nothing.</summary>
    public string? TrashFolder { get; init; } = trashFolder;

    /// <summary>The folder of this sync's backup stamp (the copies in <see cref="BackedUp"/> are inside it); null when the sync backed nothing up.</summary>
    public string? BackupFolder { get; init; } = backupFolder;

    /// <summary>Paths where content that changed outside the app was an earlier version the storage had already moved past (a checkout, an old pull), so the storage's version was written here (or the file deleted again) and nothing was uploaded for them. These paths also appear in <see cref="Downloaded"/> or <see cref="DeletedHere"/>.</summary>
    public IReadOnlyList<string> Stale { get; init; } = stale ?? [];

    /// <summary>Copies (in the trash folder) of local files that a download replaced with the storage's newer content. Not files the sync removed: those are <see cref="Trashed"/>.</summary>
    public IReadOnlyList<string> Replaced { get; init; } = replaced ?? [];

    /// <summary>Links (junctions or symlinks) inside the board folder that the sync did not follow: nothing is read from, written to or deleted behind them, here or in the storage.</summary>
    public IReadOnlyList<string> SkippedLinks { get; init; } = skippedLinks ?? [];

    /// <summary>Paths of files larger than the limit for one stored file (64 MB): a local file that is not uploaded, or an object in the storage that is not downloaded, left alone in both directions and never taken for a deletion. Listed again on every sync until the file shrinks or goes. The rest of the sync is not affected.</summary>
    public IReadOnlyList<string> TooLarge { get; init; } = tooLarge ?? [];

    /// <summary>The full paths of the copies this sync made in this computer's trash folder: every local file the sync removed (<see cref="DeletedHere"/>), and every file it overwrote whose content exists nowhere else (a non-text clash the storage won). The sync never erases a file on this computer.</summary>
    public IReadOnlyList<string> Trashed { get; init; } = trashed ?? [];

    internal static SyncReport Of(SyncOutcome outcome, string? message = null, SyncBlock blocked = SyncBlock.None) => new(outcome, message, [], [], [], [], [], [], [], blocked: blocked);
}

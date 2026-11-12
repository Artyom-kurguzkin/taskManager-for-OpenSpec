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
    IReadOnlyList<string>? tooLarge = null)
{
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

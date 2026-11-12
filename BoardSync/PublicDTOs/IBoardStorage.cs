namespace BoardSync.PublicDTOs;

/// <summary>
/// Where a project's board is kept so computers can share it: one small contract that the sync loop talks to.
/// Paths are forward-slash relative paths ("manifest.json", "objects/ab/abcdef"); ".." segments, absolute paths and backslashes are refused with an ArgumentException.
/// "Not there", "not modified" and "someone else wrote first" are results; a network, access or disk failure throws <see cref="BoardStorageException"/>.
/// </summary>
public interface IBoardStorage
{
    /// <summary>False when the store cannot write "only if unchanged"; the sync loop then needs its fallback.</summary>
    bool ConditionalWrites { get; }

    /// <summary>The file, or null when it is not there.</summary>
    Task<StoredFile?> Get(string path, CancellationToken ct = default);

    /// <summary><see cref="StoredFile.NotModified"/> when <paramref name="version"/> is still current, the new file when it is not, null when the file is not there.</summary>
    Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default);

    /// <summary>Writes the file when <paramref name="condition"/> holds; Written = false when someone else wrote first or the file is already there.</summary>
    Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default);

    /// <summary>Removes the file; removing what is not there is not an error.</summary>
    Task Delete(string path, CancellationToken ct = default);

    /// <summary>Every path that starts with the prefix, with its version.</summary>
    Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default);
}

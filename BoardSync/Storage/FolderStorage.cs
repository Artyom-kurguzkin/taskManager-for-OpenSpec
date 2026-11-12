using System.Security.Cryptography;
using BoardSync.PublicDTOs;
using BoardSync.Sync;

namespace BoardSync.Storage;

/// <summary>
/// A storage that is a directory (a network share, or a temp folder in tests). The version is the SHA-256 of the content.
/// A conditional write is atomic against other processes doing the same: the new content is written to a temp file next to the target,
/// then a lock file (opened with FileShare.None) is held around "check the current version, then move the temp file over the target".
/// </summary>
internal sealed class FolderStorage(string folder) : IBoardStorage
{
    private const string LockName = ".rtm-storage.lock";
    private const string TempSuffix = ".rtm-tmp";
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly AsyncLocal<TimeSpan?> LockTimeoutOverride = new();
    private static TimeSpan LockTimeout => LockTimeoutOverride.Value ?? DefaultLockTimeout;

    /// <summary>A test seam: until the result is disposed, the lock wait is <paramref name="timeout"/> for the code running from this call (not for tests running in parallel).</summary>
    internal static IDisposable OverrideLockTimeout(TimeSpan timeout)
    {
        TimeSpan? before = LockTimeoutOverride.Value;
        LockTimeoutOverride.Value = timeout;
        return new Restore(() => LockTimeoutOverride.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    private readonly string _root = Path.GetFullPath(folder);

    public bool ConditionalWrites => true;

    public Task<StoredFile?> Get(string path, CancellationToken ct = default) => Guard<StoredFile?>(async () =>
    {
        string file = Resolve(path);
        byte[]? bytes = await ReadAll(file, StorageLimits.For(path), path, ct);
        return bytes is null ? null : new StoredFile(bytes, Hash(bytes));
    }, "read", path);

    public async Task<StoredFile?> GetIfChanged(string path, string? version, CancellationToken ct = default)
    {
        StoredFile? file = await Get(path, ct);
        return file is not null && version is not null && file.Version == version ? StoredFile.NotModified : file;
    }

    public Task<PutResult> Put(string path, byte[] bytes, PutCondition condition, CancellationToken ct = default) => Guard(async () =>
    {
        string target = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + "." + Guid.NewGuid().ToString("N") + TempSuffix;
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            using (await Lock(ct))
            {
                if (condition.Kind == PutKind.OnlyIfAbsent && File.Exists(target)) return new PutResult(false, null);
                if (condition.Kind == PutKind.OnlyIfVersion)
                {
                    byte[]? current = await ReadAll(target, StorageLimits.For(path), path, ct);
                    if (current is null || Hash(current) != condition.Version) return new PutResult(false, null);
                }
                MoveWithRetry(temp, target);
                return new PutResult(true, Hash(bytes));
            }
        }
        finally { try { File.Delete(temp); } catch (IOException) { } }
    }, "write", path);

    public Task Delete(string path, CancellationToken ct = default) => Guard(async () =>
    {
        string file = Resolve(path);
        if (!Directory.Exists(Path.GetDirectoryName(file))) return 0;
        using (await Lock(ct)) File.Delete(file);
        return 0;
    }, "delete", path);

    public Task<IReadOnlyList<StoredEntry>> List(string prefix, CancellationToken ct = default) => Guard<IReadOnlyList<StoredEntry>>(async () =>
    {
        StoragePath.CheckPrefix(prefix);
        int slash = prefix.LastIndexOf('/');
        string start = slash < 0 ? _root : Path.Combine(_root, prefix[..slash].Replace('/', Path.DirectorySeparatorChar));
        List<StoredEntry> entries = [];
        if (!Directory.Exists(start)) return entries;
        if (start != _root && LocalTree.ThroughLink(_root, start)) throw LinkRefused(prefix);
        // Links inside the storage folder are not entered: what is behind one is not this storage's.
        EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string file in Directory.EnumerateFiles(start, "*", options))
        {
            string name = Path.GetFileName(file);
            if (name == LockName || name.EndsWith(TempSuffix, StringComparison.Ordinal)) continue;
            string relative = Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!relative.StartsWith(prefix, StringComparison.Ordinal)) continue;
            byte[]? bytes = await ReadAll(file, StorageLimits.ObjectMax, relative, ct);
            if (bytes is not null) entries.Add(new StoredEntry(relative, Hash(bytes)));
        }
        return entries;
    }, "list", prefix);

    private string Resolve(string path)
    {
        string file = Path.Combine(_root, StoragePath.Check(path).Replace('/', Path.DirectorySeparatorChar));
        if (LocalTree.ThroughLink(_root, file)) throw LinkRefused(path);       // a junction or symlink inside the storage folder could lead anywhere
        return file;
    }

    private static BoardStorageException LinkRefused(string path) =>
        new($"The folder storage did not use \"{path}\" because a link (junction or symlink) inside the storage folder is in the way. Remove the link, or choose the folder it points to as the storage.");

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>The file's bytes, null when it is not there. A file being replaced at this moment is retried briefly.</summary>
    private static async Task<byte[]?> ReadAll(string file, long cap, string path, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
                if (stream.Length > cap) throw StorageLimits.TooBig(path, cap);
                return await StorageLimits.ReadCapped(stream, cap, path, ct);
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) when (attempt < 50) { await Task.Delay(10, ct); }
        }
    }

    private static void MoveWithRetry(string temp, string target)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temp, target, overwrite: true); return; }
            catch (IOException) when (attempt < 50) { Thread.Sleep(10); }   // a reader has the target open for a moment
            catch (UnauthorizedAccessException) when (attempt < 50) { Thread.Sleep(10); }
        }
    }

    /// <summary>Holds the storage's lock file until disposed; waits for another writer, in this process or another, up to 30 seconds.</summary>
    private async Task<IDisposable> Lock(CancellationToken ct)
    {
        string lockFile = Path.Combine(_root, LockName);
        DateTime until = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try { return new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && DateTime.UtcNow < until && Directory.Exists(_root))
            {
                await Task.Delay(Random.Shared.Next(2, 12), ct);
            }
        }
    }

    private static async Task<T> Guard<T>(Func<Task<T>> work, string what, string path)
    {
        try { return await work(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new BoardStorageException($"The folder storage could not {what} \"{path}\": {e.Message} Check that the folder exists, is reachable and can be written to.", e);
        }
    }
}

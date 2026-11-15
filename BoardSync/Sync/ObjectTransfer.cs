using System.Security.Cryptography;
using BoardSync.PublicDTOs;
using BoardSync.Storage;

namespace BoardSync.Sync;

/// <summary>An object the pass has: a file (a download in the state folder's fetch folder, or a base copy that already had the hash) whose content was checked against its hash.</summary>
internal sealed class FetchedObject(string hash, string filePath)
{
    internal string Hash { get; } = hash;
    internal string FilePath { get; } = filePath;

    /// <summary>The whole content, read now: only a merge, a conflict report or an event log needs it in memory.</summary>
    internal byte[] Bytes()
    {
        byte[] read = LocalTree.ReadOrNull(FilePath) ?? throw new SyncFailure("A downloaded file disappeared from the sync's own folder, so nothing was synced. Sync again.");
        if (SyncModels.HashOf(read) != Hash) throw new SyncFailure("A downloaded file in the sync's own folder was changed, so nothing was synced. Sync again.");
        return read;
    }
}

/// <summary>
/// Downloads the objects a pass needs: up to <see cref="Parallel"/> at a time, each streamed into a file in the state folder while its hash is computed (never held whole in memory),
/// and a base copy with the same hash is used instead of a request. The files are deleted when the pass ends.
/// </summary>
internal sealed class ObjectFetcher : IDisposable
{
    /// <summary>How many objects are in flight at once, for downloads and uploads.</summary>
    internal const int Parallel = 6;

    private readonly IBoardStorage _storage;
    private readonly SyncStateStore _store;
    private readonly string _folder;
    private readonly Dictionary<string, FetchedObject> _done = new(StringComparer.Ordinal);     // by content hash: the same content under two paths is one download

    internal ObjectFetcher(IBoardStorage storage, SyncStateStore store, string folder)
    {
        _storage = storage;
        _store = store;
        _folder = folder;
        Clean();                    // what a pass that died left behind
    }

    internal static ParallelOptions Options(CancellationToken ct) => new() { MaxDegreeOfParallelism = Parallel, CancellationToken = ct };

    public void Dispose() => Clean();

    private void Clean()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Starts the downloads of the objects the plan is going to ask for, six at a time. An object that is too large is left for the path's own visit to report.</summary>
    internal async Task Prefetch(IEnumerable<(string Path, string Hash)> wanted, CancellationToken ct)
    {
        List<(string Path, string Hash)> todo;
        lock (_done) todo = [.. wanted.DistinctBy(w => w.Hash).Where(w => !_done.ContainsKey(w.Hash))];
        if (todo.Count == 0) return;
        await System.Threading.Tasks.Parallel.ForEachAsync(todo, Options(ct), async (w, token) =>
        {
            try { await Get(w.Path, w.Hash, token); }
            catch (BoardStorageException e) when (e.IsTooLarge) { }
        });
    }

    internal async Task<FetchedObject> Get(string path, string hash, CancellationToken ct)
    {
        lock (_done) if (_done.TryGetValue(hash, out FetchedObject? known)) return known;
        FetchedObject made = FromBase(hash) ?? await Download(path, hash, ct);
        lock (_done) return _done.TryAdd(hash, made) ? made : _done[hash];
    }

    /// <summary>The state folder's copy of this content (the merge base of some path), when it is there and still hashes right: no request at all.</summary>
    private FetchedObject? FromBase(string hash)
    {
        string? file = _store.BaseFile(hash);
        return file is not null && LocalTree.HashOfFile(file) == hash ? new FetchedObject(hash, file) : null;
    }

    private async Task<FetchedObject> Download(string path, string hash, CancellationToken ct)
    {
        Directory.CreateDirectory(_folder);
        string temp = System.IO.Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string actual;
            await using (FileStream file = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                using HashingSink sink = new(file, StorageLimits.ObjectMax, path);
                bool found = await _storage.GetTo(SyncModels.ObjectPath(path, hash), sink, ct);
                if (!found)
                    throw new SyncFailure($"The storage has no stored copy of \"{path}\" (content {hash[..8]}), so nothing was synced. Another computer may have cleaned up the storage; sync again from the computer that has the file.");
                await sink.FlushAsync(ct);
                actual = sink.Finish();
            }
            if (actual != hash) throw new SyncFailure($"The stored copy of \"{path}\" in the storage is damaged, so nothing was synced.");
            return new FetchedObject(hash, temp);
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}

/// <summary>A write-only stream that passes what it gets to a file, hashes it on the way and refuses more than the cap.</summary>
internal sealed class HashingSink(Stream inner, long cap, string path) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _length;

    internal string Finish() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

    private void Count(int bytes)
    {
        _length += bytes;
        if (_length > cap) throw StorageLimits.TooBig(path, cap);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        Count(buffer.Length);
        _hash.AppendData(buffer.Span);
        await inner.WriteAsync(buffer, ct);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Count(buffer.Length);
        _hash.AppendData(buffer);
        inner.Write(buffer);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override void Flush() => inner.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hash.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Copying a file into place the way <see cref="LocalTree.WriteAtomic"/> writes bytes: a temp file next to the target, then a rename, with the hash checked on the way.</summary>
internal static class FileCopy
{
    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="target"/> through a temp file and renames it over the target. When the copied content does not hash to
    /// <paramref name="hash"/> nothing is put in place and false comes back.
    /// </summary>
    internal static bool CopyVerified(string source, string target, string hash)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        string temp = target + "." + Guid.NewGuid().ToString("N") + LocalTree.TempSuffix;
        try
        {
            string actual;
            using (FileStream from = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920))
            using (FileStream to = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
            using (IncrementalHash h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] chunk = new byte[81920];
                int read;
                while ((read = from.Read(chunk)) > 0)
                {
                    h.AppendData(chunk, 0, read);
                    to.Write(chunk, 0, read);
                }
                actual = Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();
            }
            if (actual != hash) return false;
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temp, target, overwrite: true); return true; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 40) { Thread.Sleep(25); }       // a watcher or editor has it open for a moment
            }
        }
        finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}

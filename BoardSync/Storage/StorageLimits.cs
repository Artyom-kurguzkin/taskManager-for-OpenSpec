using BoardSync.PublicDTOs;

namespace BoardSync.Storage;

/// <summary>How much a storage may hand back for one file. Enforced while the bytes arrive, not after they are all in memory.</summary>
internal static class StorageLimits
{
    // ponytail: constants; a board is a few hundred small text files. Make them settings if a board ever outgrows them.
    internal const long ManifestMax = 16L * 1024 * 1024;
    private const long DefaultObjectMax = 64L * 1024 * 1024;
    private static readonly AsyncLocal<long?> ObjectMaxOverride = new();

    /// <summary>The most one stored file (an object) may hold, and the biggest local file a sync uploads.</summary>
    internal static long ObjectMax => ObjectMaxOverride.Value ?? DefaultObjectMax;

    /// <summary>A test seam: until the result is disposed, the object cap is <paramref name="cap"/> for the code running from this call (not for tests running in parallel).</summary>
    internal static IDisposable OverrideObjectMax(long cap)
    {
        long? before = ObjectMaxOverride.Value;
        ObjectMaxOverride.Value = cap;
        return new Restore(() => ObjectMaxOverride.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }
    /// <summary>A bucket listing page or an error body.</summary>
    internal const long Listing = ManifestMax;

    /// <summary>The ceiling for the file at <paramref name="path"/>: the manifest is small, an object may be a bit larger.</summary>
    internal static long For(string path) => path == "manifest.json" || path.EndsWith("/manifest.json", StringComparison.Ordinal) ? ManifestMax : ObjectMax;

    internal static BoardStorageException TooBig(string path, long cap) =>
        new($"The storage has \"{path}\" larger than {(cap >= 1024 * 1024 ? $"{cap / (1024 * 1024)} MB" : $"{cap / 1024} KB")}, which is more than a board file can be, so nothing was synced.") { IsTooLarge = true };

    /// <summary>Reads the content in chunks and stops with a storage error as soon as it passes <paramref name="cap"/> (the announced length is checked first).</summary>
    internal static async Task<byte[]> ReadCapped(HttpContent content, long cap, string path, CancellationToken ct)
    {
        if (content.Headers.ContentLength > cap) throw TooBig(path, cap);
        await using Stream stream = await content.ReadAsStreamAsync(ct);
        return await ReadCapped(stream, cap, path, ct);
    }

    internal static async Task<byte[]> ReadCapped(Stream stream, long cap, string path, CancellationToken ct)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, ct);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > cap) throw TooBig(path, cap);
            buffer.Write(chunk, 0, read);
        }
    }
}


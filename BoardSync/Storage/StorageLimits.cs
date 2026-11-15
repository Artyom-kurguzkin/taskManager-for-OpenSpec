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
        return await ReadCapped(stream, cap, path, ct, content.Headers.ContentLength);
    }

    /// <summary>
    /// Reads into one buffer: a stream that knows its length (a file) or a response that announced it fills one exact array, so the bytes are never copied a second time;
    /// without a length the buffer doubles as the bytes arrive.
    /// </summary>
    internal static async Task<byte[]> ReadCapped(Stream stream, long cap, string path, CancellationToken ct, long? announced = null)
    {
        long known = stream.CanSeek ? stream.Length - stream.Position : announced ?? 0;
        if (known > cap) throw TooBig(path, cap);
        byte[] buffer = new byte[known > 0 ? (int)Math.Min(known, Array.MaxLength) : 4096];
        byte[] one = new byte[1];
        int length = 0;
        while (true)
        {
            if (length < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length), ct);
                if (read == 0) break;
                length += read;
                if (length > cap) throw TooBig(path, cap);
                continue;
            }
            if (await stream.ReadAsync(one, ct) == 0) break;           // the buffer is full: is there more than was announced?
            if (length + 1L > cap) throw TooBig(path, cap);
            Array.Resize(ref buffer, (int)Math.Min(Math.Max(length * 2L, 4096L), Math.Min(cap, Array.MaxLength)));
            buffer[length++] = one[0];
        }
        if (length != buffer.Length) Array.Resize(ref buffer, length);          // shorter than announced, or no length known: one copy at the end, as before
        return buffer;
    }

    /// <summary>Copies the stream into <paramref name="destination"/> in chunks and stops with a storage error as soon as more than <paramref name="cap"/> bytes arrive. Returns how many.</summary>
    internal static async Task<long> ReadCapped(Stream source, Stream destination, long cap, string path, CancellationToken ct)
    {
        byte[] chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(chunk, ct);
            if (read == 0) return total;
            total += read;
            if (total > cap) throw TooBig(path, cap);
            await destination.WriteAsync(chunk.AsMemory(0, read), ct);
        }
    }
}


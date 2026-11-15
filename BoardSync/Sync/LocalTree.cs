using System.Security.Cryptography;
using BoardSync.PublicDTOs;
using BoardSync.Storage;

namespace BoardSync.Sync;

/// <summary>
/// A file the scan found: its hash and last-write time. No file keeps its bytes from the scan (nothing is held for the whole pass): they are read again when something needs them
/// (an upload, a merge, a base copy), one file at a time, and the re-read must still hash to <see cref="Hash"/>, so what is uploaded is exactly what was hashed or nothing is.
/// <see cref="Settling"/>: written so recently that the scan left it for the next pass (see <see cref="SyncOptions.SettleWindow"/>).
/// </summary>
internal sealed class LocalFile(string relative, string full, string hash, DateTime lastWriteUtc, bool settling = false)
{
    internal string Hash { get; } = hash;
    internal DateTime LastWriteUtc { get; } = lastWriteUtc;
    internal bool Settling { get; } = settling;

    internal byte[] Bytes
    {
        get
        {
            byte[] read = LocalTree.ReadOrNull(full) ?? throw new SyncFailure($"\"{relative}\" disappeared while it was being synced. Sync again.");
            if (SyncModels.HashOf(read) != Hash) throw new SyncFailure($"\"{relative}\" changed while it was being synced, so nothing was synced. Sync again in a moment.");
            return read;
        }
    }
}

/// <summary>The directory side: scanning, hashing, and writes that a reader never sees half done.</summary>
internal static class LocalTree
{
    internal const string TempSuffix = ".rtm-sync-tmp";

    /// <summary>
    /// Every file under <paramref name="root"/> (full path, board-relative path) without following links: a junction or symlink, to a file or to a folder, is not entered and not listed as a file;
    /// its board-relative path goes into <paramref name="links"/>.
    /// </summary>
    internal static IEnumerable<(string Full, string Relative, FileInfo Info)> Walk(string root, List<string> links)
    {
        if (!Directory.Exists(root)) yield break;
        Stack<DirectoryInfo> pending = new();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            foreach (FileSystemInfo entry in pending.Pop().EnumerateFileSystemInfos())
            {
                string relative = Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) links.Add(relative);
                else if (entry is DirectoryInfo directory) pending.Push(directory);
                else if (entry is FileInfo info && !IsIgnored(info.Name)) yield return (info.FullName, relative, info);     // size and time come with the listing: no second look
            }
        }
    }

    /// <summary>
    /// Files that are never synced and never scanned: what editors and the app's own stores leave while they write (*.tmp, ~$*, *.swp, .#*, *~, .rtm-*). A fixed list, not a setting.
    /// <paramref name="name"/> is the file name or a board-relative path (the last part counts).
    /// </summary>
    internal static bool IsIgnored(string name)
    {
        int slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];
        const StringComparison c = StringComparison.OrdinalIgnoreCase;
        return name.EndsWith(".tmp", c) || name.StartsWith("~$", c) || name.EndsWith(".swp", c) || name.StartsWith(".#", c) || name.EndsWith('~') || name.StartsWith(".rtm-", c);
    }

    /// <summary>Up to this size a file is hashed from one read of its bytes; a larger one is hashed as a stream. Either way nothing is kept.</summary>
    internal const int SmallFileMax = 256 * 1024;
    private const int StreamBuffer = 80 * 1024;

    internal static Dictionary<string, LocalFile> Scan(string root) => Scan(root, [], []);

    /// <summary>
    /// A file larger than the object cap (<see cref="StorageLimits.ObjectMax"/>) is not read: its path goes into <paramref name="tooLarge"/> and it is not in the result.
    /// <paramref name="onBuffer"/> (a test seam) hears the size of every buffer the scan allocates for file content: one per file that is actually read.
    /// With a <paramref name="cache"/> a file whose size and last write are the cached ones is not read at all (its cached hash is used); every file seen is recorded in it (the caller saves it).
    /// A file written less than <paramref name="settle"/> before the scan began (and put off for less than five minutes) is in the result with <see cref="LocalFile.Settling"/> set.
    /// </summary>
    internal static Dictionary<string, LocalFile> Scan(string root, List<string> links, List<string> tooLarge, Action<long>? onBuffer = null, StatCache? cache = null, TimeSpan settle = default)
    {
        Dictionary<string, LocalFile> files = new(StringComparer.Ordinal);
        long cap = StorageLimits.ObjectMax;
        cache?.BeginScan();
        foreach ((string file, string relative, FileInfo info) in Walk(root, links))
        {
            if (file.EndsWith(TempSuffix, StringComparison.Ordinal)) continue;     // left by a crash in the middle of one of our own writes
            try
            {
                long length = info.Length;                                          // looked at before anything is read
                if (length > cap) { tooLarge.Add(relative); continue; }
                DateTime written = info.LastWriteTimeUtc;
                string? hash = cache?.Lookup(relative, length, written.Ticks);
                if (hash is null)
                {
                    if (length <= SmallFileMax)
                    {
                        byte[] bytes = ReadShared(file);
                        onBuffer?.Invoke(bytes.Length);
                        if (bytes.Length > cap) { tooLarge.Add(relative); continue; }
                        hash = SyncModels.HashOf(bytes);
                    }
                    else
                    {
                        onBuffer?.Invoke(StreamBuffer);
                        (string streamed, long read) = HashStream(file, cap);
                        if (read > cap) { tooLarge.Add(relative); continue; }       // it grew while it was read
                        hash = streamed;
                    }
                }
                cache?.Record(relative, length, written.Ticks, hash);
                files[relative] = new LocalFile(relative, file, hash, written, cache?.Defer(relative, written, settle) == true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new SyncFailure($"Could not read \"{relative}\": {e.Message} Close whatever has it open, then sync again.");
            }
        }
        return files;
    }

    /// <summary>
    /// True when writing, deleting or moving <paramref name="target"/> would go through a link or out of <paramref name="root"/>: the path is not below the root, or a folder
    /// between the root and the target (or the target itself) is a junction or symlink. A part that does not exist yet is fine.
    /// </summary>
    internal static bool ThroughLink(string root, string target)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(target);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison)) return true;
        string current = fullRoot;
        foreach (string part in full[(fullRoot.Length + 1)..].Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true; }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return false; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
        }
        return false;
    }

    /// <summary>The file's bytes, null when it is not there.</summary>
    internal static byte[]? ReadOrNull(string file)
    {
        try { return ReadShared(file); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    internal static string ToLocal(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The file's hash, streamed (never the whole file in memory); null when it is not there.</summary>
    internal static string? HashOfFile(string file)
    {
        try { return HashStream(file, long.MaxValue).Hash; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    /// <summary>The hash and the length of the file, read in chunks; stops reading after more than <paramref name="stopAfter"/> bytes (the length is then more than that).</summary>
    private static (string Hash, long Length) HashStream(string file, long stopAfter)
    {
        using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, StreamBuffer);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] chunk = new byte[StreamBuffer];
        long total = 0;
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            total += read;
            if (total > stopAfter) return ("", total);
            hash.AppendData(chunk, 0, read);
        }
        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
    }

    private static byte[] ReadShared(string file)
    {
        using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Temp file in the same directory, then a move over the target.</summary>
    internal static void WriteAtomic(string file, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + "." + Guid.NewGuid().ToString("N") + TempSuffix;
        try
        {
            File.WriteAllBytes(temp, bytes);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(temp, file, overwrite: true); return; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 40) { Thread.Sleep(25); }   // a watcher or editor has it open for a moment
            }
        }
        finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    /// <summary>Removes the directories a file that is gone left empty, never the root itself. The sync never erases a board file: it moves it to the trash first (<see cref="SyncStateStore.MoveToTrash"/>).</summary>
    internal static void RemoveEmptyParents(string root, string file)
    {
        string fullRoot = Path.GetFullPath(root);
        for (string? dir = Path.GetDirectoryName(Path.GetFullPath(file)); dir is not null && !PathEquals(dir, fullRoot) && Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
        {
            if (Directory.EnumerateFileSystemEntries(dir).Any()) break;
            try { Directory.Delete(dir); } catch (IOException) { break; }
        }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar), b.TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

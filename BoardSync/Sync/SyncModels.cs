using System.Security.Cryptography;
using System.Text.Json;

namespace BoardSync.Sync;

/// <summary>One path in the manifest. A tombstone (Deleted) keeps the hash of the last content.</summary>
internal sealed class ManifestEntry
{
    public string Hash { get; set; } = "";
    public string Clock { get; set; } = "";
    public bool Deleted { get; set; }
    /// <summary>The hashes of earlier versions of this path, newest first, at most <see cref="SyncModels.ReadHistoryLimit"/> when read, <see cref="SyncModels.HistoryLimit"/> when this computer writes a new version.</summary>
    public List<string> History { get; set; } = [];

    internal ManifestEntry Copy() => new() { Hash = Hash, Clock = Clock, Deleted = Deleted, History = [.. History] };
}

internal sealed class ManifestFile
{
    public int Version { get; set; } = 1;
    public Dictionary<string, ManifestEntry> Entries { get; set; } = [];
}

/// <summary>This computer's record of the last successful sync: the manifest entries it ended with, and the storage version they came from.</summary>
internal sealed class SyncStateFile
{
    public string? StorageVersion { get; set; }
    public Dictionary<string, ManifestEntry> Entries { get; set; } = [];
}

internal static class SyncModels
{
    /// <summary>How many earlier versions a path keeps when this computer writes the manifest (staleness is also covered by seen.json and the refused upload).</summary>
    internal const int HistoryLimit = 3;
    /// <summary>How many earlier versions the parser accepts: an older client wrote up to twenty.</summary>
    internal const int ReadHistoryLimit = 20;
    internal const string ManifestPath = "manifest.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    /// <summary>The manifest is read by machines and put on every change: no indentation (the same members and names, so an older client reads it).</summary>
    private static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    internal static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Where the content with <paramref name="contentHash"/> is kept for <paramref name="path"/>: objects are named by path AND content (sha256 of the path, a 0 byte and the content hash),
    /// so the same text in two files is two objects, and an object that already exists means "this path has had this content before". Computable from the manifest alone.
    /// </summary>
    internal static string ObjectPath(string path, string contentHash)
    {
        string key = HashOf(System.Text.Encoding.UTF8.GetBytes(path + "\0" + contentHash));
        return $"objects/{key[..2]}/{key}";
    }

    internal static Dictionary<string, ManifestEntry> CopyOf(Dictionary<string, ManifestEntry> entries) =>
        new(entries.Select(e => KeyValuePair.Create(e.Key, e.Value.Copy())), StringComparer.Ordinal);

    /// <summary>Entries come out sorted so the same board always serializes to the same text.</summary>
    private static SortedDictionary<string, ManifestEntry> Sorted(Dictionary<string, ManifestEntry> entries) => new(entries, StringComparer.Ordinal);

    internal static byte[] Serialize(ManifestFile manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(new { manifest.Version, Entries = Sorted(manifest.Entries) }, Compact);

    internal static byte[] Serialize(SyncStateFile state) =>
        JsonSerializer.SerializeToUtf8Bytes(new { state.StorageVersion, Entries = Sorted(state.Entries) }, Json);

    /// <summary>What the manifest file may hold before it is checked: every member can be null or missing.</summary>
    private sealed class RawManifest
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, RawEntry?>? Entries { get; set; }
    }

    private sealed class RawEntry
    {
        public string? Hash { get; set; }
        public string? Clock { get; set; }
        public bool Deleted { get; set; }
        public List<string?>? History { get; set; }
    }

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    internal static ManifestFile ParseManifest(byte[] bytes)
    {
        try
        {
            RawManifest raw = JsonSerializer.Deserialize<RawManifest>(bytes, Json) ?? throw new JsonException("empty");
            if (raw.Version != 1) throw new SyncFailure("The board in the storage was written by a newer version of Repo Tasks. Update this app, then sync again.");
            if (raw.Entries is null) throw new SyncFailure("The manifest in the storage has no list of entries, so nothing was synced.");

            ManifestFile manifest = new() { Entries = new(StringComparer.Ordinal) };
            Dictionary<string, string> lowered = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string path, RawEntry? entry) in raw.Entries)
            {
                if (UnsafeReason(path) is { } reason) throw new SyncFailure($"The manifest in the storage names a path that cannot be used in the board folder (\"{path}\": {reason}), so nothing was synced.");
                if (!lowered.TryAdd(path, path)) throw new SyncFailure($"The manifest in the storage names \"{lowered[path]}\" and \"{path}\", which differ only by letter case and would overwrite each other, so nothing was synced.");
                if (entry is null) throw new SyncFailure($"The manifest in the storage has an empty entry for \"{path}\", so nothing was synced.");
                if (!IsHash(entry.Hash)) throw new SyncFailure($"The manifest in the storage has no valid content id for \"{path}\", so nothing was synced.");
                if (!SyncClock.IsValid(entry.Clock)) throw new SyncFailure($"The manifest in the storage has no valid clock for \"{path}\", so nothing was synced.");
                if (entry.History is null) throw new SyncFailure($"The manifest in the storage has no history for \"{path}\", so nothing was synced.");
                // ponytail: only the newest ReadHistoryLimit earlier versions are kept; a longer list is cut, not refused, so an older writer's long history still loads.
                List<string> history = [];
                foreach (string? h in entry.History.Take(ReadHistoryLimit))
                {
                    if (!IsHash(h)) throw new SyncFailure($"The manifest in the storage has an invalid earlier version of \"{path}\", so nothing was synced.");
                    history.Add(h!);
                }
                manifest.Entries[path] = new ManifestEntry { Hash = entry.Hash!, Clock = entry.Clock!, Deleted = entry.Deleted, History = history };
            }
            return manifest;
        }
        catch (JsonException e)
        {
            throw new SyncFailure($"The manifest in the storage could not be read ({e.Message}), so nothing was synced.");
        }
    }

    /// <summary>A content hash as this engine writes it: 64 lower-case hex digits.</summary>
    internal static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    internal static SyncStateFile? ParseState(byte[] bytes)
    {
        try
        {
            SyncStateFile? state = JsonSerializer.Deserialize<SyncStateFile>(bytes, Json);
            if (state is null) return null;
            state.Entries = new(state.Entries.Where(e => e.Value is not null), StringComparer.Ordinal);
            foreach (ManifestEntry entry in state.Entries.Values) entry.History ??= [];
            return state;
        }
        catch (JsonException) { return null; }    // a damaged state is the same as none: the next sync is a first connection, which backs up before it replaces
    }

    /// <summary>A relative path with forward slashes that stays inside the folder: what a manifest from the storage may name.</summary>
    internal static bool SafePath(string path) => UnsafeReason(path) is null;

    /// <summary>
    /// Why a path from the storage cannot be written into the board folder, or null: empty, backslash, colon (a drive or a data stream), leading slash, an empty, "." or ".." part,
    /// a Windows device name in any part (with or without an extension), a part ending in a dot or a space (Windows drops those, so the file would get another name).
    /// </summary>
    internal static string? UnsafeReason(string path)
    {
        if (path.Length == 0) return "empty";
        if (path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) return "not a relative path with forward slashes";
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..") return "an empty or relative part";
            if (segment[^1] is '.' or ' ') return $"part \"{segment}\" ends in a dot or a space";
            int dot = segment.IndexOf('.');
            if (DeviceNames.Contains((dot < 0 ? segment : segment[..dot]).TrimEnd(' '))) return $"part \"{segment}\" is a Windows device name";
        }
        return null;
    }
}

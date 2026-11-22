using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkItems.PublicDTOs;

namespace TaskManager.AppState;

/// <summary>What is saved of one change folder: where it is (relative to the repository, forward slashes), how its files looked when it was read, and what was parsed from them.</summary>
internal sealed class IndexFolder
{
    public string Path { get; set; } = "";
    public bool Archived { get; set; }

    /// <summary>A file of the folder was modified within a few milliseconds of the read: the folder is read once more at the next start (racy-git).</summary>
    public bool Racy { get; set; }

    public int? StoredId { get; set; }
    public List<IndexStamp> Stamps { get; set; } = [];

    /// <summary>The item as parsed, before it gets its number, its commit authors and its status fallback; its Path is left empty (the folder's path is above, relative).</summary>
    public WorkItem Item { get; set; } = null!;
}

/// <summary>One file or folder of a change folder: its path inside it (forward slashes), and for a file its size and modified time (UTC, ISO 8601). A folder has neither.</summary>
internal sealed class IndexStamp
{
    public string Path { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Size { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mtime { get; set; }
}

/// <summary>The whole file of one repository.</summary>
internal sealed class IndexData
{
    public int Version { get; set; }

    /// <summary>The repository's absolute path: the one place it is kept, to know the file is of this repository and not of a copy of it elsewhere.</summary>
    public string Repo { get; set; } = "";

    public string SavedAt { get; set; } = "";

    /// <summary>The project's statuses the items were shown with (an item with a status outside the list shows in Backlog).</summary>
    public List<string> Statuses { get; set; } = [];

    public List<IndexFolder> Folders { get; set; } = [];
}

/// <summary>
/// The index of a repository, kept between runs: one JSON file per repository in <see cref="Helpers.Paths.Index"/> (a test gives another folder). It is a cache and nothing else: a file that is missing, unreadable,
/// of another format version or of another place is dropped and the repository is read as if there were none. Portable: relative paths with forward slashes, times in UTC, no platform call; the repository's own
/// absolute path is the only absolute path in it.
/// </summary>
internal static class IndexFile
{
    /// <summary>Goes up when what is saved changes shape; an index of another version is dropped.</summary>
    internal const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        IgnoreReadOnlyProperties = true,   // WorkItem.AgentUnreviewed is computed
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new LocalTimeAsUtc() },
    };

    /// <summary>The file of a repository: its folder name for the eye and a hash of its full path (lower-cased, as the settings key it) so two repositories of one name do not meet.</summary>
    internal static string PathFor(string folder, string repo)
    {
        string full = System.IO.Path.GetFullPath(repo).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..12];
        string name = new([.. System.IO.Path.GetFileName(full).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')]);
        return System.IO.Path.Combine(folder, (name.Length > 0 ? name : "repo") + "-" + hash + ".json");
    }

    /// <summary>The saved index of the repository, or null when there is none that fits (it is deleted when it does not: missing, unreadable, another version, another place).</summary>
    internal static IndexData? Load(string folder, string repo)
    {
        string file = PathFor(folder, repo);
        try
        {
            if (!File.Exists(file)) return null;
            IndexData? data;
            using (FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                data = JsonSerializer.Deserialize<IndexData>(stream, Options);
            if (data is { Version: FormatVersion } && data.Folders.All(f => f.Item is not null) && Same(data.Repo, repo)) return data;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        Delete(folder, repo);
        return null;
    }

    /// <summary>Writes the file whole through a temporary one, so a reader or a crash never sees half of it.</summary>
    internal static void Save(string folder, IndexData data)
    {
        Directory.CreateDirectory(folder);
        string file = PathFor(folder, data.Repo), temp = file + "." + Environment.ProcessId + ".tmp";
        try
        {
            using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None)) JsonSerializer.Serialize(stream, data, Options);
            File.Move(temp, file, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch (IOException) { } }
    }

    internal static void Delete(string folder, string repo)
    {
        try { File.Delete(PathFor(folder, repo)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool Same(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a).TrimEnd(System.IO.Path.DirectorySeparatorChar), System.IO.Path.GetFullPath(b).TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>A time of this computer's zone is saved in UTC and comes back in the zone the app runs in then; a time with no zone (read from a file's text) is left as it is.</summary>
    private sealed class LocalTimeAsUtc : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            DateTime at = reader.GetDateTime();
            return at.Kind == DateTimeKind.Utc ? at.ToLocalTime() : at;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value);
    }
}

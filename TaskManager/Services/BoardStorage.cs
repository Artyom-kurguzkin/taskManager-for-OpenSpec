using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoardSync;
using BoardSync.PublicDTOs;
using OpenSpecStore;
using YamlDotNet.Serialization;

namespace TaskManager.Services;

/// <summary>
/// One setting a provider needs. <paramref name="OnThisComputer"/>: kept per computer in the app's data folder, never in the repository (keys and
/// secrets); otherwise it is the address and goes into the project's settings in the repository, so the next computer learns it from git.
/// <paramref name="Secret"/>: a password input, never shown back, protected for the current user.
/// </summary>
public sealed record StorageField(string Key, string Label, string Placeholder = "", bool Secret = false, bool Optional = false, bool OnThisComputer = false);

/// <summary>
/// A kind of connected storage: what it is called, the fields the settings ask for and how to open an <see cref="IBoardStorage"/> from the saved
/// values. The Settings page renders everything from this, so a new provider (SharePoint, Azure Blob, Google Drive) is one more descriptor.
/// </summary>
public sealed record StorageProvider(string Id, string Name, string Hint, IReadOnlyList<StorageField> Fields, Func<IReadOnlyDictionary<string, string>, IBoardStorage> Open);

/// <summary>The project's saved choice: which provider and the address values kept in the repository (<c>x-rtm.boardStorage</c> in openspec/config.yaml).</summary>
public sealed record BoardStorageConfig(string Provider, IReadOnlyDictionary<string, string> Values);

public static class StorageProviders
{
    public static readonly StorageProvider S3 = new("s3", "S3-compatible (Cloudflare R2, Amazon S3, MinIO)",
        "The endpoint is the account's S3 address, like https://<account>.r2.cloudflarestorage.com. The key is kept on this computer only, never in the repository.",
        [
            new("endpoint", "Endpoint", "https://<account>.r2.cloudflarestorage.com"),
            new("bucket", "Bucket", "board"),
            new("prefix", "Prefix", "optional, a folder in the bucket", Optional: true),
            new("accessKey", "Access key ID", OnThisComputer: true),
            new("secretKey", "Secret access key", Secret: true, OnThisComputer: true),
        ],
        v => BoardSyncApi.S3Storage(v["endpoint"].Trim(), v["bucket"].Trim(), v.GetValueOrDefault("prefix"), v["accessKey"].Trim(), v["secretKey"].Trim()));

    public static readonly StorageProvider Folder = new("folder", "Folder",
        "Any folder: a network share, a folder another program syncs. Everyone who opens this project uses the same path.",
        [new("path", "Path", @"\\server\share\board")],
        v => BoardSyncApi.FolderStorage(v["path"].Trim()));

    public static IReadOnlyList<StorageProvider> Builtin { get; } = [S3, Folder];
}

/// <summary>
/// What the repository's settings ask for, shown to the user before anything happens: <paramref name="Line"/> is the question, <paramref name="Detail"/> says
/// plainly where the board would go (a network share on a host; an S3 host, bucket and prefix).
/// </summary>
public sealed record StorageProposal(string Name, string Summary, string? Detail)
{
    public string Line => $"This project asks to keep its board in step through: {Name}  {Summary}. Allow it on this computer?";
}

/// <summary>
/// The rules for an address and the words for it. Only strings and paths are looked at: nothing here opens a file, a folder or a connection, so the rules
/// can run on a repository's proposal before the user has allowed it.
/// </summary>
internal static class AddressRules
{
    internal const string ProviderS3 = "s3", ProviderFolder = "folder";

    /// <summary>Why this address cannot be used (what to change), or null. Empty values are not judged: they are "not filled in yet".</summary>
    internal static string? Validate(string root, string providerId, IReadOnlyDictionary<string, string> v) => providerId switch
    {
        ProviderFolder => v.TryGetValue("path", out string? path) ? Folder(root, path.Trim()) : null,
        ProviderS3 => S3(v.GetValueOrDefault("endpoint")?.Trim(), v.GetValueOrDefault("bucket")?.Trim(), v.GetValueOrDefault("prefix")?.Trim()),
        _ => null,
    };

    private static string? Folder(string root, string path)
    {
        if (path.Length == 0) return null;
        if (!Path.IsPathFullyQualified(path))
            return $"The folder \"{path}\" is not an absolute path. Use the whole path, like C:\\boards\\project or \\\\server\\share\\board: a relative path would depend on where the app was started, so it is refused.";
        string full, board, git;
        try
        {
            full = Path.GetFullPath(path);
            board = Path.GetFullPath(Path.Combine(root, "openspec", "changes"));
            git = Path.GetFullPath(Path.Combine(root, ".git"));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return $"The folder \"{path}\" is not a valid path."; }
        if (Same(full, board) || Inside(full, board)) return $"The folder \"{path}\" is inside this project's board (openspec/changes): the board cannot be kept in itself. Choose a folder outside the project.";
        if (Inside(board, full)) return $"The folder \"{path}\" contains this project's board (openspec/changes): the board cannot be kept in a folder that holds it. Choose a folder outside the project.";
        if (Same(full, git) || Inside(full, git)) return $"The folder \"{path}\" is inside the repository's .git folder. Choose a folder outside the project.";
        return null;
    }

    private static string? S3(string? endpoint, string? bucket, string? prefix)
    {
        if (!string.IsNullOrEmpty(endpoint))
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                return $"\"{endpoint}\" is not a web address. Use the storage's endpoint, for example https://<account>.r2.cloudflarestorage.com.";
            if (uri.UserInfo.Length > 0 || endpoint.Contains('@'))
                return "The endpoint must not contain a user name or key (https://key:secret@host). Remove it: the keys are entered in their own boxes and stay on this computer.";
            if (endpoint.Contains('?') || endpoint.Contains('#'))
                return "The endpoint must not contain a query (?...) or a fragment (#...). Use only the address, like https://<account>.r2.cloudflarestorage.com.";
            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
                return $"The endpoint \"{endpoint}\" uses http, which sends the board unencrypted. Use https:// (http is accepted only for localhost).";
        }
        foreach ((string label, string? value) in new[] { ("bucket", bucket), ("prefix", prefix) })
            foreach (string segment in (value ?? "").Split('/', '\\'))
                if (segment is "." or "..") return $"The {label} \"{value}\" has a \"{segment}\" part, which is not allowed. Use plain names.";
        return null;
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Comparison);

    /// <summary>True when <paramref name="inner"/> lies below <paramref name="outer"/>.</summary>
    private static bool Inside(string inner, string outer) => inner.StartsWith(outer.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, Comparison);

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>One value in a form that does not change with spelling, so the same address always has the same hash.</summary>
    internal static string Normalise(string providerId, string key, string value)
    {
        value = value.Trim();
        switch (providerId, key)
        {
            case (ProviderS3, "endpoint"): return value.TrimEnd('/').ToLowerInvariant();
            case (ProviderS3, "prefix"): return value.Trim('/');
            case (ProviderFolder, "path"):
                try { return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant() : value; }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return value; }
            default: return value;
        }
    }

    /// <summary>Provider and every address value, normalised: what an approval and a key's binding are about.</summary>
    internal static string Hash(string providerId, IReadOnlyDictionary<string, string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerId + "\n" + string.Join("\n", values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key + "=" + Normalise(providerId, v.Key, v.Value)))))).ToLowerInvariant();

    internal static StorageProposal Describe(StorageProvider provider, IReadOnlyDictionary<string, string> v)
    {
        string name = provider.Name.Split(" (")[0];
        if (provider.Id == ProviderFolder)
        {
            string path = v.GetValueOrDefault("path", "").Trim();
            string? host = path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ? path.TrimStart('\\', '/').Split('\\', '/')[0] : null;
            return new(name, path, host is null ? "The board would be kept in that folder on this computer." : $"That is a network share on {host}: Windows connects to {host} and signs in to it as you, and the whole board is sent there.");
        }
        if (provider.Id == ProviderS3)
        {
            string endpoint = v.GetValueOrDefault("endpoint", "").Trim(), bucket = v.GetValueOrDefault("bucket", "").Trim(), prefix = v.GetValueOrDefault("prefix", "").Trim();
            string host = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ? uri.Host : endpoint;
            return new(name, $"address {endpoint}, bucket {bucket}, prefix {(prefix.Length == 0 ? "(none)" : prefix)}",
                $"The board would be sent to the host {host}, into the bucket \"{bucket}\", under the prefix \"{(prefix.Length == 0 ? "(none)" : prefix)}\", signed with this computer's key.");
        }
        return new(name, string.Join(", ", v.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key} {x.Value}")), null);
    }
}

/// <summary>
/// How the app reads and writes its own small files in the data folder (credentials.json, approval.json, edited-in-app.txt, device-id, the server's write log).
/// Another handle on such a file (the sync reading it on another thread, the settings page, a virus scanner) must not break a save: readers share
/// read, write and delete, and the atomic write retries a few times with a growing delay before it gives up with an <see cref="IOException"/> or
/// <see cref="UnauthorizedAccessException"/> the caller turns into a message.
/// </summary>
internal static class DataFile
{
    private const int Attempts = 8;

    private static void Pause(int attempt) => Thread.Sleep(15 * (attempt + 1));   // 15, 30, ... 105 ms: about half a second in all

    /// <summary>The whole text, or null when there is no such file. A file that stays locked throws after a few short tries.</summary>
    public static string? ReadText(string file)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 3) { Thread.Sleep(10 * (attempt + 1)); }
        }
    }

    /// <summary>The non-empty lines of the file; none when there is no such file.</summary>
    public static string[] ReadLines(string file) =>
        ReadText(file)?.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray() ?? [];

    /// <summary>Writes a temp file and moves it over the target, again after a short while when something has the target open.</summary>
    public static void WriteAtomic(string file, string text)
    {
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try { File.WriteAllText(temp, text); File.Move(temp, file, overwrite: true); return; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < Attempts - 1) { Pause(attempt); }
            }
        }
        finally { try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }

    /// <summary>Deletes the file (nothing to do when it is not there), trying again when something has it open; a file that stays locked throws.</summary>
    public static void Delete(string file)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Delete(file); return; }
            catch (DirectoryNotFoundException) { return; }   // no folder, no file: nothing to delete (and nothing to wait for)
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < Attempts - 1) { Pause(attempt); }
        }
    }
}

/// <summary>
/// Per project: whether the board is shared through a connected storage, with which provider, and the values it needs.
/// The provider and its address live in the repository (openspec/config.yaml, <c>x-rtm.boardStorage</c>) and are only a PROPOSAL there: nothing is opened,
/// touched or sent until this computer has approved that exact provider and address (approval.json in the project's folder, a hash over both). Choosing or
/// changing the storage in this app's settings approves what was just saved. Keys and secrets live under the app's data folder, per computer and per project,
/// all protected with the Windows per-user protection (DPAPI) and bound to the address they were entered for (credentials.json holds that address' hash):
/// when the address changes underneath, the keys count as not set.
/// Nothing is created in the data folder until a value is saved or a sync runs.
/// </summary>
public sealed class BoardStorageSettings(string dataFolder, IReadOnlyList<StorageProvider> providers)
{
    private const string Section = "boardStorage";
    private static readonly IDeserializer Reader = new DeserializerBuilder().Build();
    private static readonly ISerializer Writer = OpenSpecStoreApi.YamlWriter(model: false, flowLists: false);
    private readonly object files = new();   // this process's reads and writes of a project's credentials.json and approval.json take turns
    private const string UnreadableMessage = "The board storage settings in config.yaml could not be read. Choose \"This folder only\" and connect again, or fix the file.";

    public string DataFolder { get; } = dataFolder;
    public IReadOnlyList<StorageProvider> Providers { get; } = providers;

    /// <summary>Raised after a setting was saved.</summary>
    public event Action? Changed;

    public StorageProvider? Find(string id) => Providers.FirstOrDefault(p => p.Id == id);

    private static string ConfigFile(string root) => Path.Combine(root, "openspec", "config.yaml");

    // ── the repository's part ──

    private sealed record ConfigRead(BoardStorageConfig? Config, bool Unreadable);

    /// <summary>The section as written: none, readable, or unreadable (an empty or malformed provider, a section that is not a flat list of values, anything unexpected).</summary>
    private static ConfigRead ReadConfig(string root)
    {
        // The file is read so that a writer is never locked out (the store's own read holds it with no sharing for writers, so a write meeting it fails with "in use" / "access denied"),
        // and a file that stays busy is as it was a moment ago: a read that collides with a write never disconnects the project or hides the switch.
        string file = ConfigFile(root);
        string? text;
        try { text = DataFile.ReadText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return LastRead.GetValueOrDefault(file) ?? new(null, true); }
        ConfigRead read = Parse(text);
        LastRead[file] = read;
        return read;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ConfigRead> LastRead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The lines under a top-level key (de-indented) or its inline value; null when absent. The same text-level rule the store uses for the x-rtm block.</summary>
    private static string? SectionOf(string yaml, string key)
    {
        string[] lines = yaml.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int start = Array.FindIndex(lines, l => l.StartsWith(key + ":", StringComparison.Ordinal));
        if (start < 0) return null;
        string inline = lines[start][(key.Length + 1)..].Trim();
        if (inline.Length > 0) return inline + "\n";
        int end = start + 1;
        while (end < lines.Length && (lines[end].Length == 0 || char.IsWhiteSpace(lines[end][0]) || lines[end].StartsWith("- ", StringComparison.Ordinal))) end++;
        return string.Concat(lines[(start + 1)..end].Select(l => (l.StartsWith("  ") ? l[2..] : l.TrimStart()) + "\n"));
    }

    private static ConfigRead Parse(string? text)
    {
        try
        {
            if (text is null || SectionOf(text, "x-rtm") is not { } block || SectionOf(block, Section) is not { } yaml || yaml.Trim().Length == 0) return new(null, false);
            Dictionary<string, string?> values = Reader.Deserialize<Dictionary<string, string?>>(yaml) ?? [];
            if (!values.Remove("provider", out string? provider)) return new(null, false);
            if (string.IsNullOrWhiteSpace(provider)) return new(null, true);
            return new(new BoardStorageConfig(provider.Trim(), values.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value!)), false);
        }
        catch (Exception) { return new(null, true); }   // a repository's file never stops the app: whatever went wrong, the section counts as unreadable
    }

    /// <summary>The saved choice, or null for "this folder only" (also when the section cannot be read: see <see cref="Unreadable"/>).</summary>
    public BoardStorageConfig? Config(string root) => ReadConfig(root).Config;

    /// <summary>True when config.yaml has a board-storage section that cannot be read.</summary>
    public bool Unreadable(string root) => ReadConfig(root).Unreadable;

    /// <summary>Chooses connected storage with this provider (values of the same name are kept when the provider changes, but only from a choice this computer allowed). Approves what is saved.</summary>
    public (bool Success, string Message) Connect(string root, string providerId)
    {
        if (Find(providerId) is not { } provider) return (false, $"Unknown storage \"{providerId}\"");
        BoardStorageConfig? old = Config(root);
        bool trusted = old is not null && Find(old.Provider) is { } oldProvider && IsApproved(root, oldProvider, AddressValues(oldProvider, old));
        Dictionary<string, string> values = new() { ["provider"] = provider.Id };
        if (trusted)
        {
            foreach (StorageField f in provider.Fields.Where(f => !f.OnThisComputer))
                if (old!.Values.GetValueOrDefault(f.Key) is { Length: > 0 } v) values[f.Key] = v;
            if (old!.Values.TryGetValue(AttachmentsKey, out string? off)) values[AttachmentsKey] = off;   // the switch is the project's, whichever provider carries it
        }
        if (!trusted || old!.Provider != provider.Id) ForgetCredentials(root);   // the keys of another provider, or of a storage this computer never allowed, are not kept
        (bool ok, string message) = Save(root, values);
        if (ok && !TryApprove(root, out string? failure)) return (false, failure!);
        if (ok) Changed?.Invoke();
        return (ok, message);
    }

    /// <summary>Back to "this folder only": the section goes from the repository; this computer's approval and keys for the project go too.</summary>
    public (bool Success, string Message) Disconnect(string root)
    {
        (bool ok, string message) = WriteSection(root, null);
        if (ok) { ForgetCredentials(root); ForgetApproval(root); Changed?.Invoke(); }
        return (ok, message);
    }

    /// <summary>Saves one value: an address value into the repository, a key or secret onto this computer. Empty removes it. Refused while the saved storage is only a proposal.</summary>
    public (bool Success, string Message) SetValue(string root, string key, string? value)
    {
        if (Config(root) is not { } config || Find(config.Provider) is not { } provider) return (false, "This project is not connected to a storage");
        if (provider.Fields.FirstOrDefault(f => f.Key == key) is not { } field) return (false, $"{provider.Name} has no setting \"{key}\"");
        if (!IsApproved(root, provider, AddressValues(provider, config))) return (false, "This storage is only proposed by the repository. Allow it first, or choose \"This folder only\" and connect again.");
        string clean = (value ?? "").Trim();
        if (field.OnThisComputer)
        {
            try { SaveComputer(root, field, clean); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (false, "The " + field.Label.ToLowerInvariant() + " could not be saved on this computer: " + e.Message); }
            Changed?.Invoke();
            return (true, "");
        }
        string? before = AddressHash(root);
        Dictionary<string, string> values = new(config.Values) { ["provider"] = config.Provider };
        if (clean.Length == 0) values.Remove(key); else values[key] = clean;
        (bool ok, string message) = Save(root, values);
        if (!ok) return (ok, message);
        string? after = AddressHash(root);
        if (before is not null && after is not null) RebindCredentials(root, before, after);   // an address edited here takes this computer's keys along
        if (!TryApprove(root, out string? approvalFailure)) return (false, approvalFailure!);
        Changed?.Invoke();
        return (true, Evaluate(root) is { Proposal: null, Problem: { } refused } ? refused : "");   // saved, and told when the address cannot be used as it is
    }

    /// <summary>Writes the section. The store is told what is on disk now first (a pull since its last read is no conflict), and a file another reader or writer has just now is tried again a few times; a failure is a message, never an exception.</summary>
    private static (bool Success, string Message) WriteSection(string root, string? yaml)
    {
        string file = ConfigFile(root);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                OpenSpecStoreApi.ReadText(file);
                return OpenSpecStoreApi.WriteRtmSection(file, Section, yaml);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 7) return (false, "The settings file could not be written: " + e.Message);
                Thread.Sleep(15 * (attempt + 1));
            }
        }
    }

    private (bool Success, string Message) Save(string root, Dictionary<string, string> values) =>
        WriteSection(root, Writer.Serialize(values).Replace("\r\n", "\n")
            .Replace(AttachmentsKey + ": 'false'", AttachmentsKey + ": false").Replace(AttachmentsKey + ": \"false\"", AttachmentsKey + ": false"));   // the writer quotes the text "false"; the file says a plain false

    // ── this computer's part: the approval ──

    /// <summary>This project's folder in the app's data folder: the engine's state, the keys, the approval, what the sync keeps for the user.</summary>
    public string ProjectFolder(string root) =>
        Path.Combine(DataFolder, "sync", Slug(Path.GetFileName(root.TrimEnd('\\', '/'))) + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SettingsStore.Key(root))))[..8].ToLowerInvariant());

    public string StateFolder(string root) => Path.Combine(ProjectFolder(root), "state");

    /// <summary>The state of the attachments sync: its own folder, never the board's (the engine keeps one manifest state per synced folder).</summary>
    public string AttachmentsStateFolder(string root) => Path.Combine(ProjectFolder(root), "state-attachments");

    /// <summary>
    /// Whether the project's attachments folder syncs beside the board (the switch "Sync attachments too"). On unless <c>attachments: false</c> stands under
    /// <c>x-rtm.boardStorage</c>. It is no part of the address, so turning it off or on never asks for an approval again.
    /// </summary>
    public bool SyncAttachments(string root) => !string.Equals(Config(root)?.Values.GetValueOrDefault(AttachmentsKey)?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    private const string AttachmentsKey = "attachments";

    /// <summary>
    /// What the sync remembers (state.json, the merge bases, what it saw, the deletion brake's count) is about one storage. When the address changed since the last sync, that memory
    /// is wrong for the new storage (a file that is not there would be taken for one deleted there), so it is dropped, for the board and the attachments both: the next sync is a first
    /// connection. The trash and the backups are the user's and stay. The address the memory is about is kept in state-address.txt beside the state folders; state with no such file is
    /// from a version before it and is taken to belong to the address now saved. Called before a sync of an approved, complete setting only.
    /// </summary>
    public void KeepStateForThisAddress(string root)
    {
        if (AddressHash(root) is not { } hash) return;
        string marker = Path.Combine(ProjectFolder(root), "state-address.txt");
        try
        {
            lock (files)
            {
                string? before = DataFile.ReadText(marker)?.Trim();
                if (before == hash) return;
                if (before is not null) { ForgetSyncState(StateFolder(root)); ForgetSyncState(AttachmentsStateFolder(root)); }
                Directory.CreateDirectory(ProjectFolder(root));
                DataFile.WriteAtomic(marker, hash);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // the next sync tries again; a sync with the old memory is what happened before this existed
    }

    /// <summary>Drops what a sync remembers in its state folder (what it synced, the merge bases, what it saw, the deletion count); the trash and the backups stay. The next sync is a first connection.</summary>
    public static void ForgetSyncState(string stateFolder)
    {
        foreach (string file in new[] { "state.json", "seen.json", "pending.json", "deletions.json" }) DataFile.Delete(Path.Combine(stateFolder, file));
        if (Directory.Exists(Path.Combine(stateFolder, "base"))) Directory.Delete(Path.Combine(stateFolder, "base"), true);
    }

    /// <summary>Turns "Sync attachments too" on or off for the project. Off is written as <c>attachments: false</c>; on removes the line. Needs a connected storage; asks nothing of the approval, which is about the address.</summary>
    public (bool Success, string Message) SetSyncAttachments(string root, bool on)
    {
        if (Config(root) is not { } config) return (false, "This project is not connected to a storage");
        Dictionary<string, string> values = new(config.Values) { ["provider"] = config.Provider };
        if (on) values.Remove(AttachmentsKey); else values[AttachmentsKey] = "false";
        (bool ok, string message) = Save(root, values);
        if (ok) Changed?.Invoke();
        return (ok, message);
    }

    private static string Slug(string name) => new([.. name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')]);

    private string ApprovalFile(string root) => Path.Combine(ProjectFolder(root), "approval.json");

    private static Dictionary<string, string> AddressValues(StorageProvider provider, BoardStorageConfig config)
    {
        Dictionary<string, string> values = [];
        foreach (StorageField f in provider.Fields.Where(f => !f.OnThisComputer))
            if (config.Values.GetValueOrDefault(f.Key) is { } v && v.Trim().Length > 0) values[f.Key] = v.Trim();
        return values;
    }

    /// <summary>The hash of the saved provider and address, or null when nothing (usable) is saved.</summary>
    public string? AddressHash(string root) =>
        Config(root) is { } config && Find(config.Provider) is { } provider ? AddressRules.Hash(provider.Id, AddressValues(provider, config)) : null;

    private sealed class Approval { public string? Address { get; set; } }

    private bool IsApproved(string root, StorageProvider provider, IReadOnlyDictionary<string, string> address)
    {
        try
        {
            string? text;
            lock (files) text = DataFile.ReadText(ApprovalFile(root));
            return text is not null && JsonSerializer.Deserialize<Approval>(text)?.Address == AddressRules.Hash(provider.Id, address);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }

    private void ApproveCurrent(string root)
    {
        if (AddressHash(root) is not { } hash) return;
        lock (files)
        {
            Directory.CreateDirectory(ProjectFolder(root));
            DataFile.WriteAtomic(ApprovalFile(root), JsonSerializer.Serialize(new Approval { Address = hash }));
        }
    }

    /// <summary>Approves what is saved; a file that cannot be written is a message, never an exception.</summary>
    private bool TryApprove(string root, out string? failure)
    {
        try { ApproveCurrent(root); failure = null; return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failure = "The approval could not be saved on this computer: " + e.Message; return false; }
    }

    /// <summary>Allows the storage the repository's settings name on this computer: from now on this provider and address may sync.</summary>
    public (bool Success, string Message) Approve(string root)
    {
        if (Config(root) is not { } config || Find(config.Provider) is not { } provider) return (false, "This project is not connected to a storage");
        if (AddressRules.Validate(root, provider.Id, AddressValues(provider, config)) is { } problem) return (false, problem);
        if (!TryApprove(root, out string? failure)) return (false, failure!);
        Changed?.Invoke();
        return (true, "");
    }

    private void ForgetApproval(string root)
    {
        try { lock (files) DataFile.Delete(ApprovalFile(root)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>What the repository's settings ask for when this computer has not allowed it (yet or any more); null when nothing is asked or it is allowed or cannot be asked.</summary>
    public StorageProposal? Proposal(string root) => Evaluate(root).Proposal;

    // ── this computer's part: keys and secrets ──

    private string CredentialsFile(string root) => Path.Combine(ProjectFolder(root), "credentials.json");

    private sealed class Credentials
    {
        public Dictionary<string, string> Plain { get; set; } = [];   // only what an older version wrote: moved to Protected when read
        public Dictionary<string, string> Protected { get; set; } = [];
        public string? Address { get; set; }                          // the address hash these keys were entered for
    }

    /// <summary>The file's content; a file that is not there or is not ours counts as empty. <paramref name="strict"/>: a file that cannot be read (something holds it) throws instead, so a save never starts from "empty" and drops the other keys.</summary>
    private Credentials LoadRaw(string root, bool strict = false)
    {
        try
        {
            string? text;
            lock (files) text = DataFile.ReadText(CredentialsFile(root));
            return text is null ? new() : JsonSerializer.Deserialize<Credentials>(text) ?? new();
        }
        catch (JsonException) { return new(); }
        catch (Exception e) when (!strict && e is IOException or UnauthorizedAccessException) { return new(); }
    }

    /// <summary>The credentials as saved. A file an older version wrote (plain key id, no address) is rewritten protected and bound to the address now saved, which this computer allowed when it entered the keys.</summary>
    private Credentials LoadComputer(string root, bool strict = false)
    {
        lock (files)
        {
            Credentials all = LoadRaw(root, strict);
            if (all.Address is not null || (all.Plain.Count == 0 && all.Protected.Count == 0)) return all;
            if (AddressHash(root) is not { } hash) return all;   // nothing to bind to: the keys wait
            foreach ((string key, string plain) in all.Plain) all.Protected[key] = Protect(plain);
            all.Plain.Clear();
            all.Address = hash;
            try { Directory.CreateDirectory(ProjectFolder(root)); DataFile.WriteAtomic(CredentialsFile(root), JsonSerializer.Serialize(all)); ApproveCurrent(root); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return all;
        }
    }

    private void SaveComputer(string root, StorageField field, string value)
    {
        lock (files)
        {
            Credentials all = LoadComputer(root, strict: true);
            string? hash = AddressHash(root);
            if (all.Address != hash) { all.Protected.Clear(); all.Plain.Clear(); all.Address = hash; }   // keys of another address are dropped: entering a key binds the keys to the address now saved
            if (value.Length == 0) all.Protected.Remove(field.Key); else all.Protected[field.Key] = Protect(value);
            Directory.CreateDirectory(ProjectFolder(root));
            DataFile.WriteAtomic(CredentialsFile(root), JsonSerializer.Serialize(all));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(CredentialsFile(root), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>The user edited the address in this app: keys that were bound to the old address are bound to the new one.</summary>
    private void RebindCredentials(string root, string before, string after)
    {
        lock (files)
        {
            Credentials all = LoadComputer(root);
            if (all.Address != before || before == after) return;
            all.Address = after;
            try { DataFile.WriteAtomic(CredentialsFile(root), JsonSerializer.Serialize(all)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private void ForgetCredentials(string root)
    {
        try { lock (files) DataFile.Delete(CredentialsFile(root)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A value kept on this computer (decrypted), for building the storage; null when it is not set or was entered for another address. The pages never show a secret.</summary>
    public string? ComputerValue(string root, string key)
    {
        Credentials all = LoadComputer(root);
        if (all.Address is null || all.Address != AddressHash(root)) return null;
        return all.Protected.TryGetValue(key, out string? sealedValue) ? Unprotect(sealedValue) : null;
    }

    /// <summary>True when keys are saved for an address other than the one now saved (the address changed underneath them).</summary>
    private bool KeysAreForAnotherAddress(string root)
    {
        Credentials all = LoadComputer(root);
        return all.Protected.Count > 0 && all.Address != AddressHash(root);
    }

    private static string Protect(string value) =>
        OperatingSystem.IsWindows() ? "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser))
            : "file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value));   // no per-user protection here: the file is readable by this user only

    private static string? Unprotect(string stored)
    {
        try
        {
            if (stored.StartsWith("dpapi:", StringComparison.Ordinal) && OperatingSystem.IsWindows())
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[6..]), null, DataProtectionScope.CurrentUser));
            if (stored.StartsWith("file:", StringComparison.Ordinal)) return Encoding.UTF8.GetString(Convert.FromBase64String(stored[5..]));
        }
        catch (Exception e) when (e is CryptographicException or FormatException) { }   // another Windows user or another machine wrote it: it counts as not set
        return null;
    }

    /// <summary>An access key id as any message may show it: only its last four characters.</summary>
    public static string Mask(string id) => id.Length >= 8 ? "…" + id[^4..] : "…";

    /// <summary>Takes this project's key id and secret out of a message (a key id stays as its last four characters).</summary>
    public string Scrub(string root, string text)
    {
        try
        {
            if (text.Length == 0) return text;
            if (ComputerValue(root, "accessKey") is { Length: > 0 } id) text = text.Replace(id, Mask(id), StringComparison.Ordinal);
            if (ComputerValue(root, "secretKey") is { Length: > 0 } secret) text = text.Replace(secret, "…", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException) { }
        return text;
    }

    // ── both ──

    /// <summary>The value of a field as the settings show it: the address from the repository, a key from this computer; null for a secret (never shown back).</summary>
    public string? Value(string root, StorageField field) =>
        field.Secret ? null : field.OnThisComputer ? ComputerValue(root, field.Key) : Config(root)?.Values.GetValueOrDefault(field.Key);

    public bool IsSet(string root, string key)
    {
        if (Config(root) is not { } config || Find(config.Provider)?.Fields.FirstOrDefault(f => f.Key == key) is not { } field) return false;
        return (field.OnThisComputer ? ComputerValue(root, key) : config.Values.GetValueOrDefault(key)) is { Length: > 0 };
    }

    private sealed record Evaluation(StorageProvider? Provider, Dictionary<string, string> Address, string? Problem, StorageProposal? Proposal);

    /// <summary>Everything that can be said about the saved choice without this computer's keys: unreadable, unknown, incomplete, refused, or only proposed.</summary>
    private Evaluation Evaluate(string root)
    {
        Dictionary<string, string> address = [];
        try
        {
            ConfigRead read = ReadConfig(root);
            if (read.Unreadable) return new(null, address, UnreadableMessage, null);
            if (read.Config is not { } config) return new(null, address, null, null);
            if (Find(config.Provider) is not { } provider) return new(null, address, $"This project uses \"{config.Provider}\" storage, which this version of the app does not know.", null);
            address = AddressValues(provider, config);
            if (provider.Fields.Any(f => !f.OnThisComputer && !f.Optional && !address.ContainsKey(f.Key))) return new(provider, address, null, null);   // incomplete: the caller says what is missing
            if (AddressRules.Validate(root, provider.Id, address) is { } refused) return new(provider, address, refused, null);
            if (!IsApproved(root, provider, address))
            {
                StorageProposal proposal = AddressRules.Describe(provider, address);
                return new(provider, address, proposal.Line, proposal);
            }
            return new(provider, address, null, null);
        }
        catch (Exception) { return new(null, address, UnreadableMessage, null); }
    }

    private (StorageProvider? Provider, Dictionary<string, string> Values, string? Problem) Resolve(string root)
    {
        Evaluation e = Evaluate(root);
        if (e.Provider is null || e.Problem is not null) return (e.Provider, e.Address, e.Problem);
        try
        {
            Dictionary<string, string> values = new(e.Address);
            foreach (StorageField f in e.Provider.Fields.Where(f => f.OnThisComputer))
                if (ComputerValue(root, f.Key) is { Length: > 0 } v) values[f.Key] = v;
            List<StorageField> missing = [.. e.Provider.Fields.Where(f => !f.Optional && !values.ContainsKey(f.Key))];
            if (missing.Count == 0) return (e.Provider, values, null);
            string names = string.Join(", ", missing.Select(f => f.Label));
            if (!missing.All(f => f.OnThisComputer)) return (e.Provider, values, $"Fill in {names} to start syncing");
            return (e.Provider, values, KeysAreForAnotherAddress(root) ? $"Waiting for {names} for the new address on this computer" : $"Waiting for {names} on this computer");
        }
        catch (Exception) { return (null, [], UnreadableMessage); }
    }

    /// <summary>Why the project cannot sync yet (nothing is allowed here, a value is missing or refused, the file cannot be read), or null when it can or is not connected.</summary>
    public string? Problem(string root) => Resolve(root).Problem;

    /// <summary>The storage to sync with, or why there is none: nothing chosen is (null, null).</summary>
    public (IBoardStorage? Storage, string? Problem) Open(string root)
    {
        (StorageProvider? provider, Dictionary<string, string> values, string? problem) = Resolve(root);
        if (provider is null || problem is not null) return (null, problem);
        try { return (provider.Open(values), null); }
        catch (Exception e) when (e is not OutOfMemoryException) { return (null, Scrub(root, e.Message)); }
    }

    /// <summary>This computer's short id, made once and kept in the data folder.</summary>
    public string DeviceId()
    {
        lock (files)
        {
            if (deviceId is not null) return deviceId;
            string file = Path.Combine(DataFolder, "device-id");
            try { if (DataFile.ReadText(file)?.Trim() is { Length: > 0 } existing) return deviceId = existing; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            try { Directory.CreateDirectory(DataFolder); DataFile.WriteAtomic(file, id); return deviceId = id; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return id; }   // not kept this time: the next call tries again
        }
    }

    private string? deviceId;
}

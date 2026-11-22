using OpenSpecStore.PublicDTOs;

namespace OpenSpecStore;

internal static class Queries
{
    /// <summary>Reads a file (empty when missing) and remembers what was seen, so a later write can detect outside edits.</summary>
    internal static string ReadText(string path)
    {
        string full = Path.GetFullPath(path);
        // A write of this file in flight (on the thread pool, since the pages write there) records what it wrote itself; a read that overlapped it must not record the older text after it.
        SemaphoreSlim gate = Commands.GateOf(full);
        bool free = gate.Wait(0);
        try
        {
            string? text = OpenSpecStoreModule.WrittenText(full) ?? ReadShared(full);   // a file the app wrote and that still has the stamp it left is not opened again
            if (free) OpenSpecStoreModule.LastSeen[full] = text;
            return text ?? "";
        }
        finally { if (free) gate.Release(); }
    }

    /// <summary>The read of the writer that already holds the path's gate (<c>Commands.HoldPath</c>): nothing else records meanwhile, and the gate is not re-entrant, so it records without taking it.</summary>
    internal static string ReadTextHeld(string full)
    {
        string? text = ReadShared(full);
        OpenSpecStoreModule.LastSeen[full] = text;
        return text ?? "";
    }

    /// <summary>
    /// The file's text, or null when it is missing. Opened sharing read, write and delete, as the board server, the OpenSpec CLI and the sync
    /// hold these files too; File.ReadAllText (FileShare.Read) throws while any of them has the file open for writing.
    /// </summary>
    internal static string? ReadShared(string full)
    {
        if (!File.Exists(full)) return null;
        Log(full);
        using FileStream stream = new(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Notes the open for a test that counts them (<see cref="StoreState.ReadLog"/>); nothing in the app.</summary>
    internal static void Log(string full)
    {
        List<string>? log = OpenSpecStoreModule.State.ReadLog;
        if (log is not null) lock (log) log.Add(full);
    }

    /// <summary>The file's text without remembering it for the stale-write check: for what only looks (a worklog's newest time, a hash), never backs an edit. Null when the file is missing.</summary>
    internal static string? ReadUnrecorded(string path) { string full = Path.GetFullPath(path); return OpenSpecStoreModule.WrittenText(full) ?? ReadShared(full); }

    /// <summary>The first <paramref name="lines"/> lines of the file (null when it is missing), without remembering it: a note's frontmatter, for its created time.</summary>
    internal static string? ReadHead(string path, int lines)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) return null;
        Log(full);
        using FileStream stream = new(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        List<string> head = [];
        for (string? line; head.Count < lines && (line = reader.ReadLine()) is not null;) head.Add(line);
        return string.Join('\n', head);
    }

    internal static string? ReadRtmBlock(string yamlPath) => RtmBlock.Extract(ReadText(yamlPath));

    internal static string? ReadRtmSection(string yamlPath, string key) =>
        RtmBlock.Extract(ReadText(yamlPath)) is string block ? RtmBlock.Extract(block, key) : null;

    /// <summary>The one place the project's config file is named: <c>openspec/config.yaml</c> of the repository.</summary>
    internal static string ConfigFile(string repoRoot) => Path.Combine(repoRoot, "openspec", "config.yaml");

    internal static string? ConfigFileOf(string? repoRoot) => (repoRoot ?? OpenSpecStoreModule.RepoRoot) is string root ? ConfigFile(root) : null;

    private static readonly YamlDotNet.Serialization.IDeserializer ConfigReader = new YamlDotNet.Serialization.DeserializerBuilder()
        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    /// <summary>The open (or guest) repository at <paramref name="root"/>, the call's current one without a root; null when it is none of them (read afresh, nothing is kept for it).</summary>
    private static Repo? RepoFor(string? root)
    {
        if (root is null) return OpenSpecStoreModule.Current;
        string full = Path.GetFullPath(root);
        StoreState state = OpenSpecStoreModule.State;
        lock (state.Repos) return state.Repos.TryGetValue(full, out Repo? open) ? open : state.Guests.GetValueOrDefault(full);
    }

    /// <summary>
    /// Brings the repository's config.yaml up to date. It is read again only when something outside the app's own writes may have changed it (the watcher or a Refresh moved <see cref="Repo.Outside"/>)
    /// or the app wrote to it (the record of touched paths, asked every time: a write to another open project's config.yaml does not move this project's version). An item written by the app
    /// moves neither, so its page loads read nothing. A file read again that says the same keeps its sections. Call it holding <paramref name="c"/>.
    /// </summary>
    private static void Refresh(Repo repo, ConfigCache c)
    {
        long outside = Interlocked.Read(ref repo.Outside);
        (IReadOnlyList<string> touched, long cursor) = OpenSpecStoreModule.TouchesSince(c.Cursor);   // asked before reading: a write that lands meanwhile shows next time
        string file = Path.GetFullPath(ConfigFile(repo.Root));
        if (c.Read && c.Outside == outside && !touched.Contains(file, StringComparer.OrdinalIgnoreCase)) { c.Cursor = cursor; return; }
        string? text = null;
        c.Unavailable = null;
        try { if (File.Exists(file)) { text = ReadShared(file); repo.Owner.ConfigReads++; } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            c.Unavailable = $"openspec/config.yaml could not be opened ({e.Message.TrimEnd('.')})";
            c.Read = false;   // not kept: the next call looks again
            return;
        }
        if (!c.Read || c.Text != text) { c.Changes++; c.Sections.Clear(); }
        (c.Text, c.Read, c.Outside, c.Cursor) = (text, true, outside, cursor);
    }

    /// <summary>Moves each time the repository's config.yaml turned out different from what was read before (0 without a repository); a cache of what is derived from the file goes by it.</summary>
    internal static long ConfigVersion(string? repoRoot)
    {
        if (RepoFor(repoRoot) is not Repo repo) return 0;
        lock (repo.Config) { Refresh(repo, repo.Config); return repo.Config.Changes; }
    }

    /// <summary>
    /// The one rule for every section of config.yaml: whatever is wrong with it, the call never throws. A section that is missing or empty gives no value and no problem;
    /// one that cannot be read (not valid YAML, not the shape the caller asked for) gives no value and a problem that names it, so the caller uses its default and can say why.
    /// A file that cannot be opened right now (another process holds it) is <see cref="ConfigSection{T}.Unavailable"/>.
    /// The file is read and its sections cut out once for each time it changed, for the repositories the store has open (see <see cref="Refresh"/>); any other repository is read afresh.
    /// </summary>
    internal static ConfigSection<T> ReadConfigSection<T>(string key, string? repoRoot)
    {
        if (RepoFor(repoRoot) is Repo repo)
        {
            ConfigCache c = repo.Config;
            lock (c)
            {
                Refresh(repo, c);
                if (c.Unavailable is not null) return new(default, c.Unavailable, Unavailable: true);
                if (c.Text is null) return new(default);   // a file that is not there is not "seen as missing": a writer that creates it later is no conflict
                OpenSpecStoreModule.LastSeen[Path.GetFullPath(ConfigFile(repo.Root))] = c.Text;   // as ReadText does: a later write is checked against what this call saw
                if (!c.Sections.TryGetValue(key, out string? yaml)) c.Sections[key] = yaml = Section(c.Text, key);
                return Parse<T>(yaml, key);
            }
        }
        if (ConfigFileOf(repoRoot) is not string file || !File.Exists(file)) return new(default);   // a file that is not there is not "seen as missing": a writer that creates it later is no conflict
        string text;
        try { text = ReadText(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(default, $"openspec/config.yaml could not be opened ({e.Message.TrimEnd('.')})", Unavailable: true); }
        return Parse<T>(Section(text, key), key);
    }

    /// <summary>The text of <c>x-rtm.&lt;key&gt;</c> in a config.yaml; null when the section is missing or empty.</summary>
    private static string? Section(string text, string key) =>
        RtmBlock.Extract(text) is string block && RtmBlock.Extract(block, key) is string yaml && yaml.Trim().Length > 0 ? yaml : null;

    private static ConfigSection<T> Parse<T>(string? yaml, string key)
    {
        if (yaml is null) return new(default);
        try { return new(ConfigReader.Deserialize<T>(yaml.Replace("\r\n", "\n").Replace('\r', '\n'))); }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException or InvalidCastException or InvalidOperationException)
        {
            string reason = (e.InnerException ?? e).Message;
            reason = System.Text.RegularExpressions.Regex.Replace(reason, @"^\(Line:[^)]*\)\s*-\s*\(Line:[^)]*\):\s*", "").TrimEnd('.');
            return new(default, $"x-rtm.{key} in openspec/config.yaml could not be read ({(reason.Length > 1 && char.IsUpper(reason[0]) && char.IsLower(reason[1]) ? char.ToLowerInvariant(reason[0]) + reason[1..] : reason)}); the default is used until the file is fixed");
        }
    }
}

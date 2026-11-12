using System.Text.Json;
using OpenSpecStore;
using TaskManager.Board;

namespace TaskManager.Services;

public sealed record AppSettings(
    string Theme = "light",               // light (default) | dark | system
    string? LastRepo = null,
    string? Name = null,                  // null = use git user.name
    string DefaultView = "board",
    Dictionary<string, BoardOptions>? Boards = null,
    string? Editor = null,      // command used by "Edit in …"; null = ask next time
    List<string>? OpenRepos = null,    // repos open as tabs, in display order
    bool Hints = false,                // F shows keyboard hints on every clickable element (off until switched on)
    List<string>? HiddenProjects = null,      // tabs taken off the board and list (by Key); the side menu brings them back
    List<string>? CollapsedProjects = null,   // tabs folded to their heading on the board (by Key)
    Dictionary<string, string>? PlansDirs = null,   // where plans live, per repository (by Key); missing = DevResources/plans
    Dictionary<string, string>? AttachmentsDirs = null,   // only what this computer had saved before the folder became a project setting (openspec/config.yaml); read until the project has one, cleared when it is saved there
    bool Split = true);                // an open work item sits beside the list (half-screen) when the window is wide enough

/// <summary>Per-user settings in a JSON file (AppData/RepoTaskManager/settings.json). Host-only, not repo data.</summary>
public sealed class SettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RepoTaskManager", "settings.json");

    public AppSettings Current { get; private set; } = Load(path);

    public event Action? Changed;

    /// <summary>A detached window reads the settings but never writes the file: its changes live in memory, so two processes never race on it.</summary>
    public bool ReadOnly { get; set; }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        if (!ReadOnly)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Current, Json));
        }
        Changed?.Invoke();
    }

    public BoardOptions Board(string repo)
    {
        BoardOptions o = Current.Boards?.GetValueOrDefault(Key(repo)) ?? new BoardOptions(View: Current.DefaultView);
        return o.GroupBy == "bucket" ? o with { GroupBy = "feature" } : o;   // "bucket" was a short-lived name for "feature"; keep those saved boards working
    }

    public void SaveBoard(string repo, BoardOptions options) =>
        Update(s => s with { Boards = new Dictionary<string, BoardOptions>(s.Boards ?? []) { [Key(repo)] = options } });

    public bool ProjectHidden(string repo) => Current.HiddenProjects?.Contains(Key(repo)) ?? false;
    public bool ProjectCollapsed(string repo) => Current.CollapsedProjects?.Contains(Key(repo)) ?? false;
    public void ToggleProjectHidden(string repo) => Update(s => s with { HiddenProjects = Toggle(s.HiddenProjects, Key(repo)) });
    public void ToggleProjectCollapsed(string repo) => Update(s => s with { CollapsedProjects = Toggle(s.CollapsedProjects, Key(repo)) });
    /// <summary>The repository-relative folder that holds plans, as it is written into the agent guide (forward slashes, no trailing slash).</summary>
    public const string DefaultPlansDir = "DevResources/plans";
    public string PlansDir(string repo) => Current.PlansDirs?.GetValueOrDefault(Key(repo)) ?? DefaultPlansDir;
    public void SetPlansDir(string repo, string? dir) => Update(s => s with { PlansDirs = WithDir(s.PlansDirs, repo, dir, DefaultPlansDir) });

    /// <summary>
    /// The repository-relative folder note attachments are copied to, under wi&lt;item&gt;/n&lt;note&gt;/ (forward slashes, no trailing slash). It is a project setting
    /// (<c>x-rtm.attachmentsDir</c> in the repository's openspec/config.yaml, so every computer that shares the project uses the same folder). Reading order: the project's
    /// value, else the one this computer had saved for the repository before it was a project setting, else the default. A value that is not allowed is skipped.
    /// </summary>
    public const string DefaultAttachmentsDir = "local/task-tracking";
    private const string AttachmentsKey = "attachmentsDir";
    public string AttachmentsDir(string repo)
    {
        if (ProjectAttachmentsDir(repo) is string project) return project;
        string? mine = Current.AttachmentsDirs?.GetValueOrDefault(Key(repo));
        return mine is not null && Notes.NotesApi.CheckAttachmentsDir(mine) is { Ok: true } c ? c.Dir : DefaultAttachmentsDir;
    }

    private static readonly YamlDotNet.Serialization.IDeserializer YamlReader = new YamlDotNet.Serialization.DeserializerBuilder().Build();

    private static string ConfigFile(string repo) => Path.Combine(repo, "openspec", "config.yaml");

    private static string? ProjectAttachmentsDir(string repo)
    {
        try
        {
            if (!File.Exists(ConfigFile(repo)) || OpenSpecStoreApi.ReadRtmSection(ConfigFile(repo), AttachmentsKey) is not { } yaml) return null;
            string value = YamlReader.Deserialize<string?>(yaml) ?? "";
            return value.Trim().Length > 0 && Notes.NotesApi.CheckAttachmentsDir(value) is { Ok: true } c ? c.Dir : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException) { return null; }   // a file the app cannot read here: the next level answers
    }

    /// <summary>
    /// Saves the project's attachments folder in openspec/config.yaml (<c>attachmentsDir: &lt;folder&gt;</c> inside the x-rtm block; the default removes the line) and
    /// clears the one this computer had kept for the repository. A folder that is not allowed, or a config.yaml that cannot be written, is refused and nothing changes.
    /// It does not move any file: see <see cref="Notes.NotesApi.MoveAttachments"/>.
    /// </summary>
    public (bool Success, string Message) SetAttachmentsDir(string repo, string? dir)
    {
        (bool ok, string clean, string why) = Notes.NotesApi.CheckAttachmentsDir(dir);
        if (!ok) return (false, why);
        (bool saved, string message) = WriteProjectAttachmentsDir(repo, clean == DefaultAttachmentsDir ? null : clean);
        if (!saved) return (false, message);
        Update(s => s with { AttachmentsDirs = WithDir(s.AttachmentsDirs, repo, null, DefaultAttachmentsDir) });   // also tells the app, which follows the new folder
        return (true, "");
    }

    private static (bool Success, string Message) WriteProjectAttachmentsDir(string repo, string? dir)
    {
        string file = ConfigFile(repo);
        OpenSpecStoreApi.ReadText(file);   // the store sees what is on disk now, so a pull since the last read is not mistaken for a conflict
        if (dir is null) return OpenSpecStoreApi.WriteRtmSection(file, AttachmentsKey, null);
        string line = OpenSpecStoreApi.YamlWriter(model: false, flowLists: false).Serialize(new Dictionary<string, string> { [AttachmentsKey] = dir }).Replace("\r\n", "\n").TrimEnd('\n');
        List<string> block = [.. (OpenSpecStoreApi.ReadRtmBlock(file) ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Where(l => l.Length > 0)];
        int at = block.FindIndex(l => l.StartsWith(AttachmentsKey + ":", StringComparison.Ordinal));
        if (at < 0) block.Add(line);
        else
        {
            int end = at + 1;
            while (end < block.Count && char.IsWhiteSpace(block[end][0])) end++;   // a value hand-written over several lines goes with its key
            block.RemoveRange(at, end - at);
            block.Insert(at, line);
        }
        return OpenSpecStoreApi.WriteRtmBlock(file, string.Join('\n', block) + "\n");
    }

    private static Dictionary<string, string>? WithDir(Dictionary<string, string>? dirs, string repo, string? dir, string fallback)
    {
        string clean = (dir ?? "").Trim().Replace('\\', '/').Trim('/');
        Dictionary<string, string> copy = new(dirs ?? []);
        if (clean.Length == 0 || clean == fallback) copy.Remove(Key(repo)); else copy[Key(repo)] = clean;
        return copy.Count == 0 ? null : copy;
    }

    private static List<string> Toggle(List<string>? list, string key) => list?.Contains(key) == true ? list.Where(k => k != key).ToList() : [.. list ?? [], key];

    internal static string Key(string repo) => Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static AppSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (JsonException) { return new(); }   // unreadable file: start fresh rather than refuse to open
    }
}

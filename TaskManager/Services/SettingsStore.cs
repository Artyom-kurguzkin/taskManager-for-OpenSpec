using System.Text.Json;
using OpenSpecStore;
using TaskManager.Board;

namespace TaskManager.Services;

public sealed record AppSettings(
    string Theme = "light",               // light (default) | dark | system
    string? LastRepo = null,
    string? Name = null,                  // null = use git user.name
    Dictionary<string, BoardOptions>? Boards = null,
    string? Editor = null,      // command used by "Edit in …"; null = ask next time
    List<string>? OpenRepos = null,    // repos open as tabs, in display order
    bool Hints = false,                // F shows keyboard hints on every clickable element (off until switched on)
    List<string>? HiddenProjects = null,      // tabs taken off the board and list (by Key); the side menu brings them back
    List<string>? CollapsedProjects = null,   // tabs folded to their heading on the board (by Key)
    Dictionary<string, string>? PlansDirs = null,   // old layout only: where plans live, per repository (by Key); missing = DevResources/plans. A project with a files folder keeps them in <filesDir>/plans
    Dictionary<string, string>? AttachmentsDirs = null,   // only what this computer had saved before the folder became a project setting (openspec/config.yaml); read until the project has one, cleared when it is saved there
    bool Split = true);                // an open work item sits beside the list (half-screen) when the window is wide enough

/// <summary>
/// Per-user, per-computer settings in one JSON file (AppData/RepoTaskManager/settings.json by default): the theme, the name, and everything the user arranged for each project
/// (<see cref="BoardOptions"/>: view, grouping, sort, hidden columns, rows and projects, the list's column order and widths). Host-only, never repository data, never synced.
/// The user can move the file to a folder of their choice (<see cref="MoveTo"/>); a pointer file at the fixed default place remembers where, so a new build finds it.
/// </summary>
public sealed class SettingsStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RepoTaskManager", "settings.json");

    /// <summary>The pointer file: next to the default settings file, always, so any build finds the chosen folder from it.</summary>
    public static string DefaultPointerPath => Path.Combine(Path.GetDirectoryName(DefaultPath)!, PointerName);

    private const string PointerName = "settings-location.txt", FileName = "settings.json";

    private string path;
    private readonly string? pointer;       // where the chosen folder is written; null = no pointer (a test or a demo with its own file)
    private readonly string defaultPath;    // the file used when nothing was chosen

    /// <summary>A store on this file, with no pointer: moving it works, but nothing remembers where it went.</summary>
    public SettingsStore(string path) : this(path, null, path, null) { }

    private SettingsStore(string path, string? pointer, string defaultPath, string? notice)
    {
        this.path = path;
        this.pointer = pointer;
        this.defaultPath = defaultPath;
        Notice = notice;
        Current = Load(path);
    }

    /// <summary>
    /// The store the app starts with: the file in the folder the pointer names, else the default file. A pointer that cannot be used (unreadable, not a path, the folder or the
    /// settings file gone) falls back to the default file and <see cref="Notice"/> says so; no pointer at all is the normal case of a user who never moved the file.
    /// </summary>
    public static SettingsStore Open(string defaultPath, string pointerPath)
    {
        string? chosen = null, notice = null;
        if (File.Exists(pointerPath))
        {
            try
            {
                string folder = File.ReadAllText(pointerPath).Trim();
                string file = Path.Combine(folder, FileName);
                if (folder.Length == 0) notice = "The file that remembers where your settings are kept is empty";
                else if (!File.Exists(file)) notice = $"There is no settings file in {folder}, where they were moved";
                else chosen = file;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                notice = "The file that remembers where your settings are kept cannot be read";
            }
        }
        return new SettingsStore(chosen ?? defaultPath, pointerPath, defaultPath, notice is null ? null : notice + $"; the settings in {Path.GetDirectoryName(defaultPath)} are in use instead.");
    }

    /// <summary>The settings file in use.</summary>
    public string FilePath => path;

    /// <summary>Said on the Settings page when the chosen location could not be used and the default file is in use instead; null otherwise.</summary>
    public string? Notice { get; private set; }

    public AppSettings Current { get; private set; }

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

    /// <summary>
    /// Moves the settings file to <paramref name="folder"/> (as settings.json) and uses it from now on; the pointer is written before the old file goes, so a restart finds it (moving back to the
    /// default folder removes the pointer). A folder that is not there, that already holds a settings.json, or that cannot be written leaves everything where it was.
    /// </summary>
    public (bool Success, string Message) MoveTo(string? folder)
    {
        if (ReadOnly) return (false, "This window only reads the settings; move them from the main window.");
        string target;
        try { target = Path.Combine(Path.GetFullPath((folder ?? "").Trim()), FileName); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return (false, "That is not a folder."); }
        string targetFolder = Path.GetDirectoryName(target)!;
        if ((folder ?? "").Trim().Length == 0 || !Directory.Exists(targetFolder)) return (false, $"{(folder ?? "").Trim()} is not a folder that exists.");
        if (SamePath(target, path)) return (true, "The settings are already in that folder.");
        if (File.Exists(target)) return (false, $"There is already a {FileName} in {targetFolder}; choose another folder or take that file away.");
        try
        {
            File.WriteAllText(target, JsonSerializer.Serialize(Current, Json));
            if (pointer is not null)
            {
                try
                {
                    if (SamePath(target, defaultPath)) File.Delete(pointer);
                    else { Directory.CreateDirectory(Path.GetDirectoryName(pointer)!); File.WriteAllText(pointer, targetFolder); }
                }
                catch (Exception) { File.Delete(target); throw; }   // no pointer, no move: a restart would not find it
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (false, $"The settings could not be moved to {targetFolder}: {e.Message}");
        }
        string old = path;
        path = target;
        Notice = null;
        try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // a copy left behind is harmless: nothing reads it
        Changed?.Invoke();
        return (true, $"Moved to {targetFolder}.");
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public BoardOptions Board(string repo)
    {
        BoardOptions? saved = Current.Boards?.GetValueOrDefault(Key(repo));
        BoardOptions o = saved ?? new BoardOptions();
        if (o.GroupBy == "bucket") o = o with { GroupBy = "feature" };   // "bucket" was a short-lived name for "feature"; keep those saved boards working
        // The list has its own groups and subgroups, flat until chosen. A list saved before it had them keeps the columns and rows it was grouped by.
        bool wasList = saved is { View: "list" };
        return o with { ListGroupBy = o.ListGroupBy ?? (wasList ? o.GroupBy : BoardModel.NoSwimlane), ListSwimlane = o.ListSwimlane ?? (wasList ? o.Swimlane : BoardModel.NoSwimlane) };
    }

    public void SaveBoard(string repo, BoardOptions options) =>
        Update(s => s with { Boards = new Dictionary<string, BoardOptions>(s.Boards ?? []) { [Key(repo)] = options } });

    public bool ProjectHidden(string repo) => Current.HiddenProjects?.Contains(Key(repo)) ?? false;
    public bool ProjectCollapsed(string repo) => Current.CollapsedProjects?.Contains(Key(repo)) ?? false;
    public void ToggleProjectHidden(string repo) => Update(s => s with { HiddenProjects = Toggle(s.HiddenProjects, Key(repo)) });
    public void ToggleProjectCollapsed(string repo) => Update(s => s with { CollapsedProjects = Toggle(s.CollapsedProjects, Key(repo)) });
    /// <summary>The repository-relative folder that holds plans, as it is written into the agent guide (forward slashes, no trailing slash).</summary>
    public const string DefaultPlansDir = "DevResources/plans";
    private string LegacyPlansDir(string repo) => Current.PlansDirs?.GetValueOrDefault(Key(repo)) ?? DefaultPlansDir;

    /// <summary>
    /// The folder plans are in: <c>&lt;files folder&gt;/plans</c> when the project has a files folder, else the one this computer set (the old layout; default
    /// <see cref="DefaultPlansDir"/>). Every reader of "the plans folder" asks here.
    /// </summary>
    public string PlansDir(string repo) => Notes.NotesApi.ProjectFilesDir(repo) is string files ? files + "/" + PlansName : LegacyPlansDir(repo);
    public void SetPlansDir(string repo, string? dir) => Update(s => s with { PlansDirs = WithDir(s.PlansDirs, repo, dir, DefaultPlansDir) });

    /// <summary>
    /// The repository-relative folder note attachments are copied to, under wi&lt;item&gt;/n&lt;note&gt;/ (forward slashes, no trailing slash). It is a project setting
    /// (<c>x-rtm.attachmentsDir</c> in the repository's openspec/config.yaml, so every computer that shares the project uses the same folder). Reading order: the project's
    /// value, else the one this computer had saved for the repository before it was a project setting, else the default. A value that is not allowed is skipped.
    /// </summary>
    public const string DefaultAttachmentsDir = "local/task-tracking";
    private const string AttachmentsKey = "attachmentsDir", FilesKey = "filesDir";

    /// <summary>The project's files folder (<c>x-rtm.filesDir</c>): inside it <c>images</c> (what notes and descriptions attach) and <c>plans</c>. Default <c>attachments</c>.</summary>
    public const string DefaultFilesDir = "attachments", ImagesName = "images", PlansName = "plans";

    /// <summary>The project's files folder, or null when its config.yaml has none: then the old plans and attachments folders are in use (nothing moves by itself).</summary>
    public string? FilesDir(string repo) => Notes.NotesApi.ProjectFilesDir(repo);

    /// <summary>
    /// The folder the attachments (images) are in: <c>&lt;files folder&gt;/images</c> when the project has a files folder, else the old layout's (below). It is the folder that is
    /// git-ignored and synced with the board storage. Every reader of "the attachments folder" asks here.
    /// </summary>
    public string AttachmentsDir(string repo)
    {
        if (FilesDir(repo) is string files) return files + "/" + ImagesName;
        return LegacyAttachmentsDir(repo);
    }

    private string LegacyAttachmentsDir(string repo)
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

    /// <summary>
    /// Saves the project's files folder in openspec/config.yaml (<c>filesDir: &lt;folder&gt;</c> inside the x-rtm block; the default is written too, because a project without the line
    /// has the old layout) and clears this computer's own plans and attachments folders for the repository, which nothing reads once the project has one. It does not move any file:
    /// see <see cref="Notes.NotesApi.MoveAttachments"/> and <see cref="Notes.NotesApi.MovePlans"/>. A folder that is not allowed, or a config.yaml that cannot be written, is refused.
    /// </summary>
    public (bool Success, string Message) SetFilesDir(string repo, string? dir)
    {
        (bool ok, string clean, string why) = Notes.NotesApi.CheckFilesDir(dir);
        if (!ok) return (false, why);
        (bool saved, string message) = WriteProjectKey(repo, FilesKey, clean);
        if (!saved) return (false, message);
        Update(s => s with { PlansDirs = WithDir(s.PlansDirs, repo, null, DefaultPlansDir), AttachmentsDirs = WithDir(s.AttachmentsDirs, repo, null, DefaultAttachmentsDir) });   // also tells the app, which follows the new folders
        return (true, "");
    }

    private static (bool Success, string Message) WriteProjectAttachmentsDir(string repo, string? dir) => WriteProjectKey(repo, AttachmentsKey, dir);

    private static (bool Success, string Message) WriteProjectKey(string repo, string key, string? dir)
    {
        string file = ConfigFile(repo);
        OpenSpecStoreApi.ReadText(file);   // the store sees what is on disk now, so a pull since the last read is not mistaken for a conflict
        if (dir is null) return OpenSpecStoreApi.WriteRtmSection(file, key, null);
        string line = OpenSpecStoreApi.YamlWriter(model: false, flowLists: false).Serialize(new Dictionary<string, string> { [key] = dir }).Replace("\r\n", "\n").TrimEnd('\n');
        List<string> block = [.. (OpenSpecStoreApi.ReadRtmBlock(file) ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Where(l => l.Length > 0)];
        int at = block.FindIndex(l => l.StartsWith(key + ":", StringComparison.Ordinal));
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

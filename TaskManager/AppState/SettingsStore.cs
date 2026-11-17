using System.Text.Json;
using OpenSpecStore;
using TaskManager.Components.Pages;

namespace TaskManager.AppState;

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
    bool Split = true,                 // an open work item sits beside the list (half-screen) when the window is wide enough
    List<string>? OpenMenuSections = null);   // the side menu's Organise / Filters / Hidden sections the user opened (all folded until then); per user, not per project

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

    /// <summary>How many times the settings file was written: a test checks that an arrow key saves once, after its pause, and an unchanged one never.</summary>
    public int Writes { get; private set; }

    private readonly object fileGate = new();
    private ITimer? saveLater;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        Write();
        Changed?.Invoke();
    }

    private void Write()
    {
        lock (fileGate)
        {
            saveLater?.Dispose();   // this write holds everything a pending one would
            saveLater = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Current, Json));
            Writes++;
        }
    }

    /// <summary>
    /// Moves the settings file to <paramref name="folder"/> (as settings.json) and uses it from now on; the pointer is written before the old file goes, so a restart finds it (moving back to the
    /// default folder removes the pointer). A folder that is not there, that already holds a settings.json, or that cannot be written leaves everything where it was.
    /// </summary>
    public (bool Success, string Message) MoveTo(string? folder)
    {
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

    /// <summary>
    /// <see cref="SaveBoard"/> for the page's own arranging (an arrow key, a drop): <see cref="Current"/> changes at once, the file follows on the thread pool once the user paused
    /// for <paramref name="pause"/> (every call moves the pause on), and <see cref="Changed"/> is raised only when something besides the card and column order changed.
    /// The app calls <see cref="Flush"/> when it closes, so a pause still running then loses nothing.
    /// </summary>
    public void SaveBoardSoon(string repo, BoardOptions options, TimeProvider time, int pause = 250)
    {
        BoardOptions? was = Current.Boards?.GetValueOrDefault(Key(repo));
        Current = Current with { Boards = new Dictionary<string, BoardOptions>(Current.Boards ?? []) { [Key(repo)] = options } };
        lock (fileGate)
        {
            saveLater?.Dispose();
            saveLater = time.CreateTimer(_ =>
            {
                try { Write(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // on the timer's thread an error would end the app; the next change saves again
            }, null, TimeSpan.FromMilliseconds(pause), Timeout.InfiniteTimeSpan);
        }
        if (was is null || was with { Order = options.Order } != options) Changed?.Invoke();
    }

    /// <summary>Writes a save that <see cref="SaveBoardSoon"/> is still waiting to write, now, and cancels its timer; does nothing when none is pending. Called when the app closes.</summary>
    public void Flush()
    {
        lock (fileGate) { if (saveLater is not null) Write(); }
    }

    public bool ProjectHidden(string repo) => Current.HiddenProjects?.Contains(Key(repo)) ?? false;
    public bool ProjectCollapsed(string repo) => Current.CollapsedProjects?.Contains(Key(repo)) ?? false;
    public void ToggleProjectHidden(string repo) => Update(s => s with { HiddenProjects = Toggle(s.HiddenProjects, Key(repo)) });
    public bool MenuSectionOpen(string section) => Current.OpenMenuSections?.Contains(section) ?? false;
    public void ToggleMenuSection(string section) => Update(s => s with { OpenMenuSections = Toggle(s.OpenMenuSections, section) });
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
    public const string DefaultAttachmentsDir = OpenSpecStoreApi.DefaultAttachmentsDir;
    private const string AttachmentsKey = "attachmentsDir", FilesKey = "filesDir";

    /// <summary>The project's files folder (<c>x-rtm.filesDir</c>): inside it <c>images</c> (what notes and descriptions attach) and <c>plans</c>. Default <c>attachments</c>.</summary>
    public const string DefaultFilesDir = OpenSpecStoreApi.DefaultFilesDir, ImagesName = "images", PlansName = "plans";

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

    private static string? ProjectAttachmentsDir(string repo)
    {
        string value = OpenSpecStoreApi.ReadConfigSection<string>(AttachmentsKey, repo).Value ?? "";   // unreadable or locked reads as none: the next level answers
        return value.Trim().Length > 0 && Notes.NotesApi.CheckAttachmentsDir(value) is { Ok: true } c ? c.Dir : null;
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

    private static (bool Success, string Message) WriteProjectKey(string repo, string key, string? dir) => OpenSpecStoreApi.WriteConfigValue(key, dir, repo);   // only that key

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

using System.Text.Json;
using OpenSpecStore;
using TaskManager.Components.Pages;

namespace TaskManager.AppState;

public sealed record AppSettings(
    string Theme = "light",               // a Helpers.Themes id (light, the default; dark; novita; ayu-mirage) or system; unknown applies as light
    string? LastRepo = null,
    string? Name = null,                  // null = use git user.name
    Dictionary<string, BoardOptions>? Boards = null,
    string? Editor = null,      // command used by "Edit in …"; null = ask next time
    List<string>? OpenRepos = null,    // repos open as tabs, in display order
    bool Hints = false,                // F shows keyboard hints on every clickable element (off until switched on)
    List<string>? HiddenProjects = null,      // tabs taken off the board and list (by Key); the side menu brings them back
    List<string>? HiddenItems = null,         // work items taken off the board and list by the user (Key of the repository + "#" + the item's number): a view choice, never written into the repository
    List<string>? CollapsedProjects = null,   // tabs folded to their heading on the board (by Key)
    Dictionary<string, string>? PlansDirs = null,   // only what this computer had saved before the folder became a project setting (openspec/config.yaml plansDir); read until the project has one, cleared when it is saved there; missing = DevResources/plans
    Dictionary<string, string>? AttachmentsDirs = null,   // only what this computer had saved before the folder became a project setting (openspec/config.yaml); read until the project has one, cleared when it is saved there
    List<string>? OpenMenuSections = null,
    string? LogsDir = null,   // where the run logs, crash notes and freeze dumps go: an absolute folder; null = the app's own (Paths.Logs). Read before the settings are opened (PeekLogsDir)
    Dictionary<string, Dictionary<string, BoardOptions>>? Arrangements = null);   // per project (Key): the arrangements saved under a name, each a snapshot of the project's BoardOptions; null = none (also every file written before they existed)

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

    public void Update(Func<AppSettings, AppSettings> change) => Apply(change, log: true);

    // What the log says of a change of the settings (#wi178: one ui line per user action): the settings the user chose. Which tabs are open, the last repository, the boards' arrangements (written by SaveBoard) and the menu's folds are no news.
    private static readonly System.Reflection.PropertyInfo[] Logged = [.. typeof(AppSettings).GetProperties().Where(p => p.Name is not ("LastRepo" or "OpenRepos" or "Boards" or "OpenMenuSections" or "CollapsedProjects"))];

    private void Apply(Func<AppSettings, AppSettings> change, bool log)
    {
        AppSettings before = Current;
        Current = change(before);
        Write();
        if (log)
            foreach (System.Reflection.PropertyInfo p in Logged)
            {
                object? was = p.GetValue(before), now = p.GetValue(Current);
                if (JsonSerializer.Serialize(was) == JsonSerializer.Serialize(now)) continue;
                TaskManager.Helpers.UiLog.Action("Settings", "Update", p.Name is "Theme" or "Hints" ? $"{p.Name}={now}" : p.Name);
            }
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

    /// <summary>Saves the folder the logs go to (null or empty = the app's own). It moves no file and does not switch the log: see <see cref="Helpers.AppLog.SwitchFolder"/>.</summary>
    public void SetLogsDir(string? folder) => Update(s => s with { LogsDir = string.IsNullOrWhiteSpace(folder) ? null : folder.Trim() });

    /// <summary>
    /// The logs folder saved in the settings file the app will open (the one the pointer names, else <paramref name="defaultPath"/>), read before the store is opened because the log starts first.
    /// Any trouble (no file, no pointer, not JSON) reads as none.
    /// </summary>
    public static string? PeekLogsDir(string defaultPath, string pointerPath)
    {
        try
        {
            string file = defaultPath;
            if (File.Exists(pointerPath) && File.ReadAllText(pointerPath).Trim() is { Length: > 0 } folder && File.Exists(Path.Combine(folder, FileName))) file = Path.Combine(folder, FileName);
            return PeekLogsDir(file);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The logs folder saved in this settings file, or null when it has none or the file cannot be read.</summary>
    public static string? PeekLogsDir(string settingsFile)
    {
        try
        {
            using JsonDocument d = JsonDocument.Parse(File.ReadAllText(settingsFile));
            return d.RootElement.TryGetProperty(nameof(AppSettings.LogsDir), out JsonElement v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
        }
        catch (Exception) { return null; }
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

    /// <summary>The names of the arrangements saved for the project, alphabetical.</summary>
    public IReadOnlyList<string> ArrangementNames(string repo) => [.. (Current.Arrangements?.GetValueOrDefault(Key(repo))?.Keys.AsEnumerable() ?? []).Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Saves the project's arrangement as it is now under <paramref name="name"/> (spaces trimmed; a name already there, in any case, is the one updated). An empty name saves nothing.</summary>
    public void SaveArrangement(string repo, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        BoardOptions now = Board(repo);
        Update(s =>
        {
            Dictionary<string, Dictionary<string, BoardOptions>> all = new(s.Arrangements ?? []);
            Dictionary<string, BoardOptions> mine = new(all.GetValueOrDefault(Key(repo)) ?? []);
            string existing = mine.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.CurrentCultureIgnoreCase)) ?? name;
            mine[existing] = now;
            all[Key(repo)] = mine;
            return s with { Arrangements = all };
        });
    }

    /// <summary>Makes the saved arrangement the project's arrangement on screen (replacing it whole); false when the project has none by that name.</summary>
    public bool OpenArrangement(string repo, string name)
    {
        if (Current.Arrangements?.GetValueOrDefault(Key(repo))?.GetValueOrDefault(name) is not BoardOptions saved) return false;
        SaveBoard(repo, saved);
        return true;
    }

    public void DeleteArrangement(string repo, string name) => Update(s =>
    {
        if (s.Arrangements?.GetValueOrDefault(Key(repo)) is not { } mine || !mine.ContainsKey(name)) return s;
        Dictionary<string, Dictionary<string, BoardOptions>> all = new(s.Arrangements);
        Dictionary<string, BoardOptions> left = new(mine);
        left.Remove(name);
        if (left.Count == 0) all.Remove(Key(repo)); else all[Key(repo)] = left;
        return s with { Arrangements = all.Count == 0 ? null : all };
    });

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
    private static string ItemKey(string repo, int id) => Key(repo) + "#" + id;
    public bool ItemHidden(string repo, int id) => Current.HiddenItems?.Contains(ItemKey(repo, id)) ?? false;
    /// <summary>The numbers of the repository's work items the user hid in the app.</summary>
    public IReadOnlySet<int> HiddenItemsOf(string repo)
    {
        string prefix = Key(repo) + "#";
        return Current.HiddenItems?.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(k.AsSpan(prefix.Length), out _)).Select(k => int.Parse(k[prefix.Length..])).ToHashSet() ?? [];
    }
    /// <summary>Hides (or shows again) work items of the repository in the app, in one write; nothing in the repository changes.</summary>
    public void SetItemsHidden(string repo, IEnumerable<int> ids, bool hidden)
    {
        List<string> keys = [.. ids.Select(i => ItemKey(repo, i))];
        TaskManager.Helpers.UiLog.Action("Settings", hidden ? "Hide" : "Show", string.Join(' ', ids.Select(i => "#wi" + i)));
        Apply(s => s with { HiddenItems = Normalise([.. (s.HiddenItems ?? []).Where(k => !keys.Contains(k)), .. hidden ? keys : []]) }, log: false);
    }
    /// <summary>
    /// "Show all hidden": in one write (one <see cref="Changed"/>), the repositories' hidden work items and projects come back, the view's hidden columns, rows and grid columns come back,
    /// and the view's filters (contributor, period, changed, commit) return to their defaults, so nothing is left to keep a work item off the board.
    /// </summary>
    public void ShowAllHidden(IEnumerable<string> repos, string? viewRoot)
    {
        HashSet<string> keys = [.. repos.Select(Key)];
        Update(s =>
        {
            Dictionary<string, BoardOptions> boards = new(s.Boards ?? []);
            if (viewRoot is not null && boards.TryGetValue(Key(viewRoot), out BoardOptions? b))
            {
                BoardOptions d = new();
                boards[Key(viewRoot)] = b with { Hidden = null, Who = d.Who, Period = d.Period, Commit = d.Commit, ChangedFrom = null, ChangedTo = null, ChangedFromTime = null, ChangedToTime = null };
            }
            return s with
            {
                Boards = s.Boards is null ? null : boards,
                HiddenProjects = Normalise([.. (s.HiddenProjects ?? []).Where(k => !keys.Contains(k))]),
                HiddenItems = Normalise([.. (s.HiddenItems ?? []).Where(k => !keys.Any(r => k.StartsWith(r + "#", StringComparison.Ordinal)))]),
            };
        });
    }
    private static List<string>? Normalise(List<string> list) => list.Count == 0 ? null : [.. list.Distinct()];
    public bool ProjectCollapsed(string repo) => Current.CollapsedProjects?.Contains(Key(repo)) ?? false;
    public void ToggleProjectHidden(string repo) => Update(s => s with { HiddenProjects = Toggle(s.HiddenProjects, Key(repo)) });
    public bool MenuSectionOpen(string section) => Current.OpenMenuSections?.Contains(section) ?? false;
    public void ToggleMenuSection(string section) => Update(s => s with { OpenMenuSections = Toggle(s.OpenMenuSections, section) });
    public void ToggleProjectCollapsed(string repo) => Update(s => s with { CollapsedProjects = Toggle(s.CollapsedProjects, Key(repo)) });
    /// <summary>The repository-relative folder that holds plans, as it is written into the agent guide (forward slashes, no trailing slash).</summary>
    public const string DefaultPlansDir = "DevResources/plans";
    private string LegacyPlansDir(string repo) => Current.PlansDirs?.GetValueOrDefault(Key(repo)) ?? DefaultPlansDir;

    /// <summary>
    /// The folder plans are in. Reading order: the project's own (<c>x-rtm.plansDir</c> in openspec/config.yaml, so every computer that shares the project uses it), else
    /// <c>&lt;old files folder&gt;/plans</c> when the config still has the old one-folder key, else the one this computer had saved (the oldest layout), else
    /// <see cref="DefaultPlansDir"/>. Every reader of "the plans folder" asks here.
    /// </summary>
    public string PlansDir(string repo) => Notes.NotesApi.ProjectPlansDir(repo) ?? (FilesDir(repo) is string files ? files + "/" + PlansName : LegacyPlansDir(repo));

    /// <summary>
    /// Saves the project's plans folder in openspec/config.yaml (<c>plansDir: &lt;folder&gt;</c> inside the x-rtm block; empty means <see cref="DefaultPlansDir"/>, which removes the line
    /// unless the old one-folder key is there, whose plans folder it would bring back) and clears the one this computer had kept. A folder that is not allowed, or a config.yaml
    /// that cannot be written, is refused and nothing changes. It does not move any file: see <see cref="Notes.NotesApi.MovePlans"/>.
    /// </summary>
    public (bool Success, string Message) SetPlansDir(string repo, string? dir)
    {
        (bool ok, string clean, string why) = string.IsNullOrWhiteSpace(dir) ? (true, DefaultPlansDir, "") : Notes.NotesApi.CheckAttachmentsDir(dir);
        if (!ok) return (false, why);
        (bool saved, string message) = WriteProjectKey(repo, PlansKey, clean == DefaultPlansDir && FilesDir(repo) is null ? null : clean);
        if (!saved) return (false, message);
        Update(s => s with { PlansDirs = WithDir(s.PlansDirs, repo, null, DefaultPlansDir) });   // also tells the app, which follows the new folder
        return (true, "");
    }

    /// <summary>
    /// The repository-relative folder note attachments are copied to, under wi&lt;item&gt;/n&lt;note&gt;/ (forward slashes, no trailing slash). It is a project setting
    /// (<c>x-rtm.attachmentsDir</c> in the repository's openspec/config.yaml, so every computer that shares the project uses the same folder). Reading order: the project's
    /// value, else the one this computer had saved for the repository before it was a project setting, else the default. A value that is not allowed is skipped.
    /// </summary>
    public const string DefaultAttachmentsDir = OpenSpecStoreApi.DefaultAttachmentsDir;
    private const string AttachmentsKey = "attachmentsDir", PlansKey = "plansDir";

    /// <summary>The names of <c>images</c> and <c>plans</c> inside the old one-folder key (<c>x-rtm.filesDir</c>), which is still read for a repository that has it but never written.</summary>
    public const string ImagesName = "images", PlansName = "plans";

    /// <summary>The project's old one-folder key, or null when its config.yaml has none.</summary>
    private static string? FilesDir(string repo) => Notes.NotesApi.ProjectFilesDir(repo);

    /// <summary>
    /// The folder the attachments (images) are in: the project's own (<c>x-rtm.attachmentsDir</c>), else <c>&lt;old files folder&gt;/images</c> when the config still has the old
    /// one-folder key, else this computer's (the oldest layout), else the default. It is the folder that is git-ignored and synced with the board storage. Every reader of
    /// "the attachments folder" asks here.
    /// </summary>
    public string AttachmentsDir(string repo)
    {
        if (ProjectAttachmentsDir(repo) is string project) return project;
        if (FilesDir(repo) is string files) return files + "/" + ImagesName;
        string? mine = Current.AttachmentsDirs?.GetValueOrDefault(Key(repo));
        return mine is not null && Notes.NotesApi.CheckAttachmentsDir(mine) is { Ok: true } c ? c.Dir : DefaultAttachmentsDir;
    }

    private static string? ProjectAttachmentsDir(string repo)
    {
        string value = OpenSpecStoreApi.ReadConfigSection<string>(AttachmentsKey, repo).Value ?? "";   // unreadable or locked reads as none: the next level answers
        return value.Trim().Length > 0 && Notes.NotesApi.CheckAttachmentsDir(value) is { Ok: true } c ? c.Dir : null;
    }

    /// <summary>
    /// Saves the project's attachments folder in openspec/config.yaml (<c>attachmentsDir: &lt;folder&gt;</c> inside the x-rtm block; the default removes the line, unless the old
    /// one-folder key is there, whose images folder it would bring back) and clears the one this computer had kept for the repository. A folder that is not allowed, or a config.yaml that cannot be written, is refused and nothing changes.
    /// It does not move any file: see <see cref="Notes.NotesApi.MoveAttachments"/>.
    /// </summary>
    public (bool Success, string Message) SetAttachmentsDir(string repo, string? dir)
    {
        (bool ok, string clean, string why) = Notes.NotesApi.CheckAttachmentsDir(dir);
        if (!ok) return (false, why);
        (bool saved, string message) = WriteProjectKey(repo, AttachmentsKey, clean == DefaultAttachmentsDir && FilesDir(repo) is null ? null : clean);
        if (!saved) return (false, message);
        Update(s => s with { AttachmentsDirs = WithDir(s.AttachmentsDirs, repo, null, DefaultAttachmentsDir) });   // also tells the app, which follows the new folder
        return (true, "");
    }

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

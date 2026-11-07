using System.Text.Json;
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
    Dictionary<string, string>? AttachmentsDirs = null);   // where note attachments are copied, per repository (by Key); missing = local/task-tracking

/// <summary>Per-user settings in a JSON file (AppData/RepoTaskManager/settings.json). Host-only, not repo data.</summary>
public sealed class SettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RepoTaskManager", "settings.json");

    public AppSettings Current { get; private set; } = Load(path);

    public event Action? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(Current, Json));
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

    /// <summary>The repository-relative folder note attachments are copied to, under wi&lt;item&gt;/n&lt;note&gt;/ (forward slashes, no trailing slash).</summary>
    public const string DefaultAttachmentsDir = "local/task-tracking";
    public string AttachmentsDir(string repo) => Current.AttachmentsDirs?.GetValueOrDefault(Key(repo)) ?? DefaultAttachmentsDir;
    public void SetAttachmentsDir(string repo, string? dir) => Update(s => s with { AttachmentsDirs = WithDir(s.AttachmentsDirs, repo, dir, DefaultAttachmentsDir) });

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

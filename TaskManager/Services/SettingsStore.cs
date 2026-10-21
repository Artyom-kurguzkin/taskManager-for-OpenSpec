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
    List<string>? OpenRepos = null);   // repos open as tabs, in display order

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
        return o.GroupBy == "feature" ? o with { GroupBy = "bucket" } : o;   // "feature" was renamed to "bucket"; keep old saved boards working
    }

    public void SaveBoard(string repo, BoardOptions options) =>
        Update(s => s with { Boards = new Dictionary<string, BoardOptions>(s.Boards ?? []) { [Key(repo)] = options } });

    internal static string Key(string repo) => Path.GetFullPath(repo).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static AppSettings Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (JsonException) { return new(); }   // unreadable file: start fresh rather than refuse to open
    }
}

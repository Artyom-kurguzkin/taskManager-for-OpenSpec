using System.Collections.Concurrent;

namespace OpenSpecStore;

/// <summary>One open repository: its content version (readers cache against it) and the watcher on its openspec/ folder.</summary>
internal sealed class Repo(string root)
{
    public string Root { get; } = root;
    public string OpenSpec => Path.Combine(Root, "openspec");
    /// <summary>Goes up whenever the repo's content may have changed (own write, outside edit).</summary>
    public long Version;
    public FileSystemWatcher? Watcher;
    public Timer? Debounce;
    public long Opened;   // order of opening (a dictionary reuses freed slots, so its order is not that)
}

/// <summary>
/// Process-wide state: the open repositories (several at once, each with its own version and watcher), which of them the
/// module Apis read and write right now (<see cref="Current"/>), what the app last read, and its own recent writes.
/// </summary>
internal static class OpenSpecStoreModule
{
    internal static readonly Dictionary<string, Repo> Repos = new(StringComparer.OrdinalIgnoreCase);
    internal static Repo? Current;
    internal static event Action? Changed;

    internal static string? RepoRoot => Current?.Root;
    internal static long Version => Current is { } c ? Interlocked.Read(ref c.Version) : 0;
    internal static void Bump() { if (Current is { } c) Interlocked.Increment(ref c.Version); }

    /// <summary>Content of each file as the app last read or wrote it; null = the file did not exist.</summary>
    internal static readonly ConcurrentDictionary<string, string?> LastSeen = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DateTime> OwnWrites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan OwnWriteWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>Tells the watcher that this path is about to change by the app's own hand, so it does not raise <see cref="Changed"/> for it. Does not bump the version: see <see cref="Touch"/>.</summary>
    internal static void Own(string path)
    {
        string full = Path.GetFullPath(path);
        OwnWrites[full] = DateTime.UtcNow;
        if (Path.GetDirectoryName(full) is string dir) OwnWrites[dir] = DateTime.UtcNow;
    }

    /// <summary>Own plus Touch, for a change the app is about to cause outside its own file writes (a CLI run).</summary>
    internal static void MarkOwn(string path)
    {
        Own(path);
        Touch(path);
    }

    private static readonly List<string> Touches = [];

    /// <summary>
    /// Records that these paths changed by the app's own hand, then bumps the version once. Readers that cache per folder ask
    /// <see cref="TouchesSince"/> which folders to read again, because a timestamp and a size can miss two quick edits.
    /// Call it after the change is on disk.
    /// </summary>
    internal static void Touch(params string[] paths)
    {
        lock (Touches) Touches.AddRange(paths.Select(Path.GetFullPath));
        Bump();
    }

    /// <summary>The paths touched after position <paramref name="cursor"/>, and the position to ask from next time.</summary>
    internal static (IReadOnlyList<string> Paths, long Cursor) TouchesSince(long cursor)
    {
        lock (Touches) return (Touches.Skip((int)Math.Min(cursor, Touches.Count)).ToList(), Touches.Count);
    }

    internal static void StartWatching(Repo repo)
    {
        repo.Watcher = new FileSystemWatcher(repo.OpenSpec)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        repo.Watcher.Changed += OnFileSystemEvent;
        repo.Watcher.Created += OnFileSystemEvent;
        repo.Watcher.Deleted += OnFileSystemEvent;
        repo.Watcher.Renamed += OnFileSystemEvent;
        repo.Watcher.EnableRaisingEvents = true;
    }

    internal static void StopWatching(Repo repo)
    {
        repo.Watcher?.Dispose();
        repo.Watcher = null;
        repo.Debounce?.Dispose();
        repo.Debounce = null;
    }

    private static void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        Repo? repo;
        lock (Repos) repo = Repos.Values.FirstOrDefault(r => ReferenceEquals(r.Watcher, sender));
        if (repo is null) return;   // late event from a watcher that was already stopped (repo closed)
        // A folder's "Changed" only echoes a change to a file inside it (even a read can trigger it); the file event covers it.
        if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
        if (OwnWrites.TryGetValue(Path.GetFullPath(e.FullPath), out DateTime at) && DateTime.UtcNow - at < OwnWriteWindow) return;
        repo.Debounce?.Dispose();
        repo.Debounce = new Timer(_ =>
        {
            Interlocked.Increment(ref repo.Version);
            // a timer callback that throws ends the process; files still moving are the usual cause, and the next event fires again
            try { Changed?.Invoke(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }, null, 300, Timeout.Infinite);
    }
}

using System.Collections.Concurrent;

namespace OpenSpecStore;

/// <summary>One open repository: its content version (readers cache against it) and the watcher on its openspec/ folder.</summary>
internal sealed class Repo(string root, StoreState owner)
{
    public string Root { get; } = root;
    public string OpenSpec => Path.Combine(Root, "openspec");
    /// <summary>The set of open repositories this one belongs to: its watcher reports there.</summary>
    public StoreState Owner { get; } = owner;
    /// <summary>Goes up whenever the repo's content may have changed (own write, outside edit).</summary>
    public long Version;
    public FileSystemWatcher? Watcher;
    public Timer? Debounce;
    public long Opened;   // order of opening (a dictionary reuses freed slots, so its order is not that)
}

/// <summary>Which repositories are open, which one is current, what the app last read of their files, and who listens for outside edits.</summary>
internal sealed class StoreState
{
    public readonly Dictionary<string, Repo> Repos = new(StringComparer.OrdinalIgnoreCase);
    public Repo? Current;
    public Action? Changed;
    /// <summary>Content of each file as the app last read or wrote it; null = the file did not exist.</summary>
    public readonly ConcurrentDictionary<string, string?> LastSeen = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Every path the app wrote, deleted or moved, in order (see <see cref="OpenSpecStoreModule.Touch"/>).</summary>
    public readonly List<string> Touches = [];
}

/// <summary>
/// Process-wide state: the open repositories (several at once, each with its own version and watcher), which of them the
/// module Apis read and write right now (<see cref="Current"/>), what the app last read, and its own recent writes.
/// The app has one such state. A test runs against its own (<see cref="Isolated"/>), so tests that run at the same time never see each other's repositories.
/// </summary>
internal static class OpenSpecStoreModule
{
    private static readonly StoreState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<StoreState?> Isolated = new();

    internal static StoreState State => Isolated.Value ?? Shared;

    internal static Dictionary<string, Repo> Repos => State.Repos;
    internal static Repo? Current { get => State.Current; set => State.Current = value; }
    internal static event Action? Changed
    {
        add { StoreState s = State; lock (s.Repos) s.Changed += value; }
        remove { StoreState s = State; lock (s.Repos) s.Changed -= value; }
    }

    internal static string? RepoRoot => Current?.Root;
    internal static long Version => Current is { } c ? Interlocked.Read(ref c.Version) : 0;
    internal static void Bump() { if (Current is { } c) Interlocked.Increment(ref c.Version); }

    /// <summary>Content of each file as the app last read or wrote it; null = the file did not exist.</summary>
    internal static ConcurrentDictionary<string, string?> LastSeen => State.LastSeen;

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

    /// <summary>
    /// Records that these paths changed by the app's own hand, then bumps the version once. Readers that cache per folder ask
    /// <see cref="TouchesSince"/> which folders to read again, because a timestamp and a size can miss two quick edits.
    /// Call it after the change is on disk.
    /// </summary>
    internal static void Touch(params string[] paths)
    {
        List<string> touches = State.Touches;
        lock (touches) touches.AddRange(paths.Select(Path.GetFullPath));
        Bump();
    }

    /// <summary>The paths touched after position <paramref name="cursor"/>, and the position to ask from next time.</summary>
    internal static (IReadOnlyList<string> Paths, long Cursor) TouchesSince(long cursor)
    {
        List<string> touches = State.Touches;
        lock (touches) return (touches.Skip((int)Math.Min(cursor, touches.Count)).ToList(), touches.Count);
    }

    internal static void StartWatching(Repo repo)
    {
        FileSystemWatcher watcher = new(repo.OpenSpec)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        void OnEvent(object _, FileSystemEventArgs e) => OnFileSystemEvent(repo, watcher, e);
        watcher.Changed += OnEvent;
        watcher.Created += OnEvent;
        watcher.Deleted += OnEvent;
        watcher.Renamed += OnEvent;
        repo.Watcher = watcher;
        watcher.EnableRaisingEvents = true;
    }

    internal static void StopWatching(Repo repo)
    {
        repo.Watcher?.Dispose();
        repo.Watcher = null;
        repo.Debounce?.Dispose();
        repo.Debounce = null;
    }

    private static void OnFileSystemEvent(Repo repo, FileSystemWatcher sender, FileSystemEventArgs e)
    {
        lock (repo.Owner.Repos) if (!ReferenceEquals(repo.Watcher, sender)) return;   // late event from a watcher that was already stopped (repo closed)
        // A folder's "Changed" only echoes a change to a file inside it (even a read can trigger it); the file event covers it.
        if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
        if (OwnWrites.TryGetValue(Path.GetFullPath(e.FullPath), out DateTime at) && DateTime.UtcNow - at < OwnWriteWindow) return;
        repo.Debounce?.Dispose();
        repo.Debounce = new Timer(_ =>
        {
            Interlocked.Increment(ref repo.Version);
            Action? changed;
            lock (repo.Owner.Repos) changed = repo.Owner.Changed;
            StoreState? before = Isolated.Value;
            Isolated.Value = ReferenceEquals(repo.Owner, Shared) ? null : repo.Owner;   // the listeners run against the state the repository is open in
            // a timer callback that throws ends the process; files still moving are the usual cause, and the next event fires again
            try { changed?.Invoke(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { Isolated.Value = before; }
        }, null, 300, Timeout.Infinite);
    }
}

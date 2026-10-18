using System.Collections.Concurrent;

namespace OpenSpecStore;

/// <summary>Process-wide state: the open repo, what the app last read, its own recent writes, and the watcher.</summary>
internal static class OpenSpecStoreModule
{
    internal static string? RepoRoot;
    internal static event Action? Changed;

    /// <summary>Goes up whenever repo content may have changed (own write, outside edit, repo switch). Readers cache against it.</summary>
    internal static long Version;
    internal static void Bump() => Interlocked.Increment(ref Version);

    /// <summary>Content of each file as the app last read or wrote it; null = the file did not exist.</summary>
    internal static readonly ConcurrentDictionary<string, string?> LastSeen = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DateTime> OwnWrites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan OwnWriteWindow = TimeSpan.FromMilliseconds(1500);
    private static FileSystemWatcher? watcher;
    private static Timer? debounce;

    internal static void MarkOwn(string path)
    {
        Bump();
        string full = Path.GetFullPath(path);
        OwnWrites[full] = DateTime.UtcNow;
        if (Path.GetDirectoryName(full) is string dir) OwnWrites[dir] = DateTime.UtcNow;
    }

    internal static void StartWatching(string openSpecRoot)
    {
        StopWatching();
        watcher = new FileSystemWatcher(openSpecRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += OnFileSystemEvent;
        watcher.Created += OnFileSystemEvent;
        watcher.Deleted += OnFileSystemEvent;
        watcher.Renamed += OnFileSystemEvent;
        watcher.EnableRaisingEvents = true;
    }

    internal static void StopWatching()
    {
        watcher?.Dispose();
        watcher = null;
        debounce?.Dispose();
        debounce = null;
    }

    private static void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!ReferenceEquals(sender, watcher)) return;   // late event from a watcher that was already stopped (repo switched)
        // A folder's "Changed" only echoes a change to a file inside it (even a read can trigger it); the file event covers it.
        if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
        if (OwnWrites.TryGetValue(Path.GetFullPath(e.FullPath), out DateTime at) && DateTime.UtcNow - at < OwnWriteWindow) return;
        debounce?.Dispose();
        debounce = new Timer(_ => { Bump(); Changed?.Invoke(); }, null, 300, Timeout.Infinite);
    }
}

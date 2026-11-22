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
    /// <summary>Goes up when something outside the app's own writes may have changed the content (the watcher noticed an edit, or the host said so with Refresh): what the app wrote itself is in the record of touched paths instead.</summary>
    public long Outside;
    public FileSystemWatcher? Watcher;
    public ITimer? Debounce;
    public long Opened;   // order of opening (a dictionary reuses freed slots, so its order is not that)
    /// <summary>What was read and parsed of this repository's config.yaml, kept while its content version and the file look the same (see <see cref="Queries.ReadConfigSection{T}"/>).</summary>
    public readonly ConfigCache Config = new();
    /// <summary>The stamps of the files as the last verification saw them (see <see cref="Verification"/>); also the lock of that look.</summary>
    public readonly VerifiedState Verified = new();
}

/// <summary>config.yaml as last read: its text, the sections cut out of it, and what the store knew of outside changes and of the app's own writes when it was.</summary>
internal sealed class ConfigCache
{
    public long Outside = -1, Cursor;
    public bool Read;
    public string? Text;   // null = the file is not there
    public string? Unavailable;   // why the file could not be opened at the last look (never kept: the next call looks again)
    public long Changes;   // how many times the text turned out different from before
    public readonly Dictionary<string, string?> Sections = [];   // x-rtm.<key> as YAML text (null = none); callers get a fresh object parsed from it, as they change what they are given
}

/// <summary>A file the app wrote: its text and the size and modified time (ticks, UTC) it had right after the write.</summary>
internal sealed record WrittenFile(string Text, long Length, long Ticks, long Seq);

/// <summary>Which repositories are open, which one is current, what the app last read of their files, and who listens for outside edits.</summary>
internal sealed class StoreState
{
    public readonly Dictionary<string, Repo> Repos = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Repositories a call works in without them being open (an agent's tool call on a repository that is no tab): read and written like an open one, but no watcher, and never listed as open. Guarded by <see cref="Repos"/>.</summary>
    public readonly Dictionary<string, Repo> Guests = new(StringComparer.OrdinalIgnoreCase);
    public Repo? Current;
    public Action? Changed;
    /// <summary>Where an exception goes that the watcher's timer callback catches (it runs on a pool thread, where a throw ends the process): the app sets it to its log, see <see cref="OpenSpecStoreApi.BackgroundFault"/>.</summary>
    public Action<Exception>? Fault;
    /// <summary>A test seam: false = <see cref="OpenSpecStoreModule.StartWatching"/> starts no file system watcher, so a late event from the disk cannot reload a page in the middle of a test. The app always has it on.</summary>
    public bool RealWatcher = true;
    /// <summary>How many times a config.yaml was read from disk (a test counts them).</summary>
    public int ConfigReads;
    /// <summary>How many times a verification listed the tree (a test counts them).</summary>
    public int Listings;
    /// <summary>A test seam: when set, the full path of every file the store opens to read is added to it (a test counts opens, not time). Null in the app.</summary>
    public List<string>? ReadLog;
    /// <summary>The clock of the watcher's debounce, of the own-write window and of verification (how old the last look is); a test sets a FakeTimeProvider and moves it by hand.</summary>
    public TimeProvider Time = TimeProvider.System;
    /// <summary>What the app wrote, by full path: the text and the stamp (size and modified time) the file had right after, so a reader gets the text without opening the file while the stamp still holds, and the watcher's echo of that write is recognised whenever it arrives.</summary>
    public readonly ConcurrentDictionary<string, WrittenFile> Written = new(StringComparer.OrdinalIgnoreCase);
    public long WrittenSeq;
    /// <summary>Content of each file as the app last read or wrote it; null = the file did not exist.</summary>
    public readonly ConcurrentDictionary<string, string?> LastSeen = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Every path the app wrote, deleted or moved, in order (see <see cref="OpenSpecStoreModule.Touch"/>).</summary>
    public readonly List<string> Touches = [];
    /// <summary>Held by an operation that changes several files that only make sense together, and by readers that repair what they find; see <see cref="OpenSpecStoreApi.StepsGate"/>.</summary>
    public readonly object StepsGate = new();
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
    /// <summary>A call's own repository, set for the code running from where <see cref="InRepo"/> was called (a tool call, a read of another tab); null = the process-wide current.</summary>
    private static readonly AsyncLocal<Repo?> Scoped = new();

    /// <summary>The repository the module Apis read and write for this call: the one the call was scoped to, else the process-wide current one.</summary>
    internal static Repo? Current { get => Scoped.Value ?? State.Current; set => State.Current = value; }

    /// <summary>Makes <paramref name="root"/> the current repository for the code running from here until the scope is disposed, and for that code only: other threads and calls keep theirs.
    /// An open repository is used as it is (one version, one watcher); any other becomes a guest.</summary>
    internal static IDisposable InRepo(string root)
    {
        string full = Path.GetFullPath(root);
        StoreState state = State;
        Repo repo;
        lock (state.Repos)
        {
            if (!state.Repos.TryGetValue(full, out Repo? found) && !state.Guests.TryGetValue(full, out found))
                state.Guests[full] = found = new Repo(full, state) { Opened = Commands.NextOpened() };
            repo = found;
        }
        Repo? before = Scoped.Value;
        Scoped.Value = repo;
        return new Undo(() => Scoped.Value = before);
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        private Action? undo = undo;
        public void Dispose() => Interlocked.Exchange(ref undo, null)?.Invoke();
    }
    internal static event Action? Changed
    {
        add { StoreState s = State; lock (s.Repos) s.Changed += value; }
        remove { StoreState s = State; lock (s.Repos) s.Changed -= value; }
    }

    internal static string? RepoRoot => Current?.Root;
    internal static int ConfigReads => State.ConfigReads;
    internal static long Version => Current is { } c ? Interlocked.Read(ref c.Version) : 0;
    internal static void Bump() { if (Current is { } c) Interlocked.Increment(ref c.Version); }
    /// <summary>The host says files may have changed behind the app's back (a sync pull, a tool call starting): a version bump, and what is kept apart from the app's own writes is read again.</summary>
    internal static void BumpOutside() { if (Current is { } c) { DropWritten(c.Root); Interlocked.Increment(ref c.Outside); Interlocked.Increment(ref c.Version); } }
    internal static long Outside => Current is { } c ? Interlocked.Read(ref c.Outside) : 0;

    /// <summary>What an agent's tool call has read of the files, for that call alone; null = the process-wide record, the window's.</summary>
    private static readonly AsyncLocal<ConcurrentDictionary<string, string?>?> CallSeen = new();

    /// <summary>
    /// Content of each file as the caller last read or wrote it; null = the file did not exist. The window has one record; each tool call in the same process has its own
    /// (<see cref="AsCaller"/>), so a call's write never makes the window's older read look current: the window's later write on that read is refused as stale.
    /// </summary>
    internal static ConcurrentDictionary<string, string?> LastSeen => CallSeen.Value ?? State.LastSeen;

    /// <summary>From here until disposed, what this code reads and writes is recorded apart from the window's.</summary>
    internal static IDisposable AsCaller()
    {
        ConcurrentDictionary<string, string?>? before = CallSeen.Value;
        CallSeen.Value = new(StringComparer.OrdinalIgnoreCase);
        return new Undo(() => CallSeen.Value = before);
    }

    private static readonly ConcurrentDictionary<string, DateTime> OwnWrites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan OwnWriteWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>Tells the watcher that this path is about to change by the app's own hand, so it does not raise <see cref="Changed"/> for it. Does not bump the version: see <see cref="Touch"/>.</summary>
    internal static void Own(string path)
    {
        string full = Path.GetFullPath(path);
        DateTime now = State.Time.GetUtcNow().UtcDateTime;
        OwnWrites[full] = now;
        if (Path.GetDirectoryName(full) is string dir) OwnWrites[dir] = now;
    }

    /// <summary>How many written files the store keeps the text of at most, and the largest text kept: what an item's header, tasks and worklog are, not a proposal of pages.</summary>
    private const int WrittenBound = 512, WrittenLargest = 256 * 1024;

    /// <summary>Remembers the text of a file just written and the stamp it has now (one stat, no open): the next read of it needs no open while the stamp holds. Call it after the file is in place.</summary>
    internal static void Remember(string full, string text)
    {
        StoreState state = State;
        try
        {
            FileInfo info = new(full);
            if (!info.Exists || text.Length > WrittenLargest) { state.Written.TryRemove(full, out _); return; }
            state.Written[full] = new WrittenFile(text, info.Length, info.LastWriteTimeUtc.Ticks, Interlocked.Increment(ref state.WrittenSeq));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { state.Written.TryRemove(full, out _); return; }
        if (state.Written.Count > WrittenBound)
            foreach (string old in state.Written.OrderBy(w => w.Value.Seq).Take(WrittenBound / 8).Select(w => w.Key).ToList()) state.Written.TryRemove(old, out _);
    }

    /// <summary>The text the app wrote to the file when the file still has the stamp that write left; null when it was not written by the app, or has changed since (its entry goes).</summary>
    internal static string? WrittenText(string full)
    {
        StoreState state = State;
        if (!state.Written.TryGetValue(full, out WrittenFile? w)) return null;
        if (Holds(full, w)) return w.Text;
        state.Written.TryRemove(full, out _);
        return null;
    }

    private static bool Holds(string full, WrittenFile w)
    {
        try { FileInfo info = new(full); return info.Exists && info.Length == w.Length && info.LastWriteTimeUtc.Ticks == w.Ticks; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Forgets what was written under a path (a folder or a file): it was deleted, moved, or something outside may have changed it.</summary>
    internal static void DropWritten(string path, StoreState? state = null)
    {
        state ??= State;
        string full = Path.GetFullPath(path);
        foreach (string key in state.Written.Keys.Where(k => k.Equals(full, StringComparison.OrdinalIgnoreCase) || k.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList())
            state.Written.TryRemove(key, out _);
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
        if (Current is { } repo) Verification.Adopt(repo, paths.Select(Path.GetFullPath));   // the app's own write is not an outside edit for the next verification
        Bump();
    }

    /// <summary>The verification found these files different from the stamps held: the readers read them again, as after an own write, without the stamps being taken as the app's own.</summary>
    internal static void Touched(Repo repo, IReadOnlyList<string> paths)
    {
        List<string> touches = repo.Owner.Touches;
        lock (touches) touches.AddRange(paths);
    }

    /// <summary>What the watcher's debounce runs once the files stopped moving: a verification (not a blanket re-read), which raises <see cref="StoreState.Changed"/> when a file differs.</summary>
    internal static void OnSettled(Repo repo)
    {
        StoreState? before = Isolated.Value;
        Isolated.Value = ReferenceEquals(repo.Owner, Shared) ? null : repo.Owner;   // the listeners run against the state the repository is open in
        try { Verification.Verify(repo, TimeSpan.Zero); }
        finally { Isolated.Value = before; }
    }

    /// <summary>The paths touched after position <paramref name="cursor"/>, and the position to ask from next time.</summary>
    internal static (IReadOnlyList<string> Paths, long Cursor) TouchesSince(long cursor)
    {
        List<string> touches = State.Touches;
        lock (touches) return (touches.Skip((int)Math.Min(cursor, touches.Count)).ToList(), touches.Count);
    }

    internal static void StartWatching(Repo repo)
    {
        if (!repo.Owner.RealWatcher) return;
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
        ObserveChange(repo, e.FullPath);
    }

    /// <summary>
    /// What the file system reported for a path. The app's own writes are no news: inside the own-write window, or, whenever the report arrives, when the file still has the size and time the
    /// app's write left (an echo that came late, from a slow disk, a virus scan or a sync client, must not undo what the write put in the caches). Anything else is an outside change: one
    /// <see cref="StoreState.Changed"/> after the quiet time of <c>300</c> ms on the state's clock.
    /// </summary>
    internal static void ObserveChange(Repo repo, string fullPath)
    {
        string full = Path.GetFullPath(fullPath);
        StoreState owner = repo.Owner;
        if (OwnWrites.TryGetValue(full, out DateTime at) && owner.Time.GetUtcNow().UtcDateTime - at < OwnWriteWindow) return;
        if (owner.Written.TryGetValue(full, out WrittenFile? w) && Holds(full, w)) return;
        repo.Debounce?.Dispose();
        // What the files stopped moving: a verification (one listing against the held stamps, see OnSettled), not a blanket re-read; the timer runs on the state's clock, so a test moves it by hand.
        // a timer callback that throws ends the process; files still moving are the usual cause (IO errors are expected: the next event fires again), anything else (a listener's bug) is handed to the app's log
        repo.Debounce = owner.Time.CreateTimer(_ =>
        {
            try { OnSettled(repo); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            catch (Exception ex) { try { owner.Fault?.Invoke(ex); } catch (Exception) { } }
        }, null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
    }
}

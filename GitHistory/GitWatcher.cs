namespace GitHistory;

/// <summary>
/// Notices a commit, checkout, fetch or config change made outside the app. It watches git's own files and nothing else: HEAD, index, packed-refs and config of the git directory
/// (not recursively), refs/ (recursively) and reftable/tables.list, never objects/ or logs/, and never *.lock files. A burst of events waits for a quiet moment, then the files are
/// compared with what they were (HEAD's commit, the size and write time of the index, packed-refs, tables.list and config): only a real difference moves <see cref="Version"/> and raises
/// the owner's Changed. A watcher error or overflow cannot know what it missed: it re-reads everything and always raises.
/// </summary>
internal sealed class GitWatcher : IDisposable
{
    /// <summary>How long git's files stay quiet before they are compared (git writes a ref, the index and a log in one command).</summary>
    internal static readonly TimeSpan QuietWait = TimeSpan.FromMilliseconds(300);

    private static readonly HashSet<string> TopLevel = new(StringComparer.OrdinalIgnoreCase) { "HEAD", "index", "packed-refs", "config" };

    private readonly string root, gitDir, common;
    private readonly GitHistoryState owner;
    private readonly GitHistoryState? captured;   // what to restore in Isolated for the callbacks (null = the process-wide one)
    private readonly GitState state;
    private readonly TimeProvider clock;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly object gate = new();
    private ITimer? timer;
    private bool disposed, forced;
    private string fingerprint, config;

    /// <summary>Moves each time the files git keeps changed (a commit, checkout, staging, a config edit); 0 until the first.</summary>
    internal long Version;
    /// <summary>The commit the current branch points at, as of the last comparison.</summary>
    internal string Head;
    /// <summary>The size and write time of the index, as of the last comparison.</summary>
    internal string IndexStamp;

    internal GitWatcher(string root, (string GitDir, string Common) dirs, GitHistoryState owner, GitHistoryState? captured, GitState state, TimeProvider clock, bool realWatchers)
    {
        this.root = root; gitDir = dirs.GitDir; common = dirs.Common;
        this.owner = owner; this.captured = captured; this.state = state; this.clock = clock;
        (fingerprint, Head, IndexStamp, config) = Snapshot();
        if (realWatchers) StartWatching();
    }

    /// <summary>The files as they are now: one comparison string, HEAD's commit, the index stamp and the config stamp.</summary>
    private (string Fingerprint, string Head, string Index, string Config) Snapshot()
    {
        string head = Queries.HeadTokenFromDisk(root);
        string headText = "";
        try { headText = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        string index = Stamp(Path.Combine(gitDir, "index")), cfg = Stamp(Path.Combine(common, "config"));
        string loose = headText.StartsWith("ref: ", StringComparison.Ordinal) ? Stamp(Path.Combine(common, headText[5..])) : "";
        string all = string.Join("|", head, headText, loose, index, Stamp(Path.Combine(common, "packed-refs")), Stamp(Path.Combine(common, "reftable", "tables.list")), cfg);
        return (all, head, index, cfg);
    }

    private static string Stamp(string path)
    {
        try { FileInfo f = new(path); return f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}" : "-"; }
        catch (IOException) { return "?"; }
        catch (UnauthorizedAccessException) { return "?"; }
    }

    private void StartWatching()
    {
        Watch(gitDir, false, "");
        if (!string.Equals(gitDir, common, StringComparison.OrdinalIgnoreCase)) Watch(common, false, "");
        Watch(Path.Combine(common, "refs"), true, "refs/");
        Watch(Path.Combine(common, "reftable"), false, "reftable/");
    }

    private void Watch(string dir, bool subdirectories, string prefix)
    {
        if (!Directory.Exists(dir)) return;
        FileSystemWatcher w = new(dir)
        {
            IncludeSubdirectories = subdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        void OnEvent(object _, FileSystemEventArgs e) { if (e.Name is string name) Raise(prefix + name); }
        w.Changed += OnEvent;
        w.Created += OnEvent;
        w.Deleted += OnEvent;
        w.Renamed += (_, e) => { if (e.Name is string name) Raise(prefix + name); };   // git renames index.lock to index: the new name is what counts
        w.Error += (_, _) => RaiseError();
        watchers.Add(w);
        w.EnableRaisingEvents = true;
    }

    private static bool Relevant(string relative)
    {
        string path = relative.Replace('\\', '/');
        if (path.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return false;
        return path.StartsWith("refs/", StringComparison.Ordinal) || path == "reftable/tables.list" || TopLevel.Contains(path);
    }

    /// <summary>What the file system watcher (or a test) reports: a path below the git directory, as "HEAD", "index" or "refs/heads/main".</summary>
    internal void Raise(string relative)
    {
        if (Relevant(relative)) Arm(false);
    }

    /// <summary>The file system watcher's error or buffer overflow: events were lost, so everything is read again.</summary>
    internal void RaiseError() => Arm(true);

    private void Arm(bool force)
    {
        lock (gate)
        {
            if (disposed) return;
            forced |= force;
            timer ??= clock.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Change(QuietWait, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        bool force;
        lock (gate)
        {
            if (disposed) return;
            force = forced;
            forced = false;
        }
        GitHistoryState? before = GitHistoryModule.Isolated.Value;
        GitHistoryModule.Isolated.Value = captured;   // the callback runs against the state the repository was opened in
        try
        {
            using (GitHistoryModule.InRepo(root))
            {
                (string now, string head, string index, string cfg) = Snapshot();
                lock (state)
                {
                    if (!force && now == fingerprint) return;
                    bool configMoved = force || cfg != config;
                    (fingerprint, Head, IndexStamp, config) = (now, head, index, cfg);
                    Interlocked.Increment(ref Version);
                    if (configMoved) { state.IsRepo = null; state.UserName = (null, false); }   // git's config (user.name, a removed .git) moved: ask again
                }
                try { owner.RaiseChanged(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // files still moving: the next change refreshes again
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Arm(false); }   // being rewritten while we looked: look again
        finally { GitHistoryModule.Isolated.Value = before; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            timer?.Dispose();
            timer = null;
        }
        foreach (FileSystemWatcher w in watchers) w.Dispose();
        watchers.Clear();
    }
}

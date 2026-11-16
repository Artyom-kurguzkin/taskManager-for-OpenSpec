using GitHistory.PublicDTOs;

namespace GitHistory;

/// <summary>What is known about one repository, kept while it stays open (and across switches between open ones): the facts that do not change with a commit. What git said about its history lives in the host's caches.</summary>
internal sealed class GitState
{
    /// <summary>Whether the root is a git work tree; checked once (each check starts a git process).</summary>
    public bool? IsRepo;

    /// <summary>git user.name, looked up once; (null, false) = not looked up yet.</summary>
    public (string? Name, bool Known) UserName;

    /// <summary>Goes up (process-wide) each time a state is made: a repository opened afresh is another session, whatever HEAD says; the host's caches of what git said are not served across it.</summary>
    private static long sessions;
    public readonly long Session = Interlocked.Increment(ref sessions);

    /// <summary>The watcher of this repository's .git (null while none runs): while it does, HEAD's commit and the index stamp are read from it, not from disk on every call.</summary>
    public GitWatcher? Watcher;
}

/// <summary>The module's state: the configured repository, what is known about each repository, and the counters tests read.</summary>
internal sealed class GitHistoryState
{
    public string? RepoRoot;
    public readonly Dictionary<string, GitState> States = new(StringComparer.OrdinalIgnoreCase);
    public int RepoChecks, UserNameLookups, Processes;
    public int LogLimit = 5000;

    /// <summary>Directory walks of the changes folder and HEAD reads from disk (counters tests read).</summary>
    public int Walks, HeadReads;

    /// <summary>The clock the watchers' quiet wait runs on (tests inject a fake one).</summary>
    public TimeProvider Clock = TimeProvider.System;

    /// <summary>Whether <see cref="GitHistoryApi.Watch"/> starts file system watchers; the test assembly turns it off so no test starts one.</summary>
    public bool RealWatchers = true;

    /// <summary>A test seam: <see cref="GitHistoryApi.Watch"/> makes a watcher with no file system watcher behind it, which the test drives by raising its events.</summary>
    public bool DrivenWatchers;

    /// <summary>Raised when a repository's git files changed outside the app (a commit, checkout, staging); components re-read.</summary>
    public event Action? Changed;
    internal void RaiseChanged() => Changed?.Invoke();
}

/// <summary>Process-wide state. The app has one such state; a test runs against its own (<see cref="Isolated"/>), so tests that run at the same time never share a repository or a counter.</summary>
internal static class GitHistoryModule
{
    private static readonly GitHistoryState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<GitHistoryState?> Isolated = new();

    internal static GitHistoryState S => Isolated.Value ?? Shared;

    /// <summary>The host's cached reads of what git says about the history (see GitHistoryApi.UseReads); null = ask git on every call. Not per test: the reads they point to find the test's own caches.</summary>
    internal static PublicDTOs.GitReads? Reads;

    /// <summary>A call's own repository and what is known about it, set for the code running from where <see cref="InRepo"/> was called; null = the configured one.</summary>
    private sealed record Scope(string Root, GitState State);
    private static readonly AsyncLocal<Scope?> Scoped = new();

    /// <summary>The repository the reads of this call go to: the one the call was scoped to, else the configured one.</summary>
    internal static string? RepoRoot { get => Scoped.Value?.Root ?? S.RepoRoot; set => S.RepoRoot = value; }

    /// <summary>What is known about each repository. Every access holds the lock on this dictionary: the UI, the file watcher's timer, sync passes and tool calls all read it.</summary>
    internal static Dictionary<string, GitState> States => S.States;
    private static readonly GitState None = new();

    /// <summary>The state of the call's repository (a throwaway one when none is configured).</summary>
    internal static GitState State
    {
        get
        {
            if (Scoped.Value is { } scoped) return scoped.State;
            Dictionary<string, GitState> states = States;
            lock (states) return S.RepoRoot is string r && states.TryGetValue(r, out GitState? s) ? s : None;
        }
    }

    /// <summary>Starts watching the repository's .git (once); null when it has none, or watchers are switched off.</summary>
    internal static GitWatcher? Watch(string root)
    {
        string full = Path.GetFullPath(root);
        GitHistoryState owner = S;
        if (!owner.RealWatchers && !owner.DrivenWatchers) return null;
        Dictionary<string, GitState> states = States;
        lock (states)
        {
            if (!states.TryGetValue(full, out GitState? state)) states[full] = state = new GitState();
            if (state.Watcher is not null) return state.Watcher;
            if (Queries.GitDirs(full) is not { } dirs) return null;
            using (InRepo(full)) return state.Watcher = new GitWatcher(full, dirs, owner, Isolated.Value, state, owner.Clock, owner.RealWatchers && !owner.DrivenWatchers);
        }
    }

    internal static void Unwatch(string root)
    {
        Dictionary<string, GitState> states = States;
        lock (states)
            if (states.TryGetValue(Path.GetFullPath(root), out GitState? state)) { state.Watcher?.Dispose(); state.Watcher = null; }
    }

    internal static int Walks { get => S.Walks; set => S.Walks = value; }
    internal static int HeadReads => S.HeadReads;

    /// <summary>Points this call's reads at <paramref name="root"/> until the scope is disposed, without touching the configured repository other calls read.</summary>
    internal static IDisposable InRepo(string root)
    {
        string full = Path.GetFullPath(root);
        Dictionary<string, GitState> states = States;
        GitState state;
        lock (states) if (!states.TryGetValue(full, out state!)) states[full] = state = new GitState();
        Scope? before = Scoped.Value;
        Scoped.Value = new Scope(full, state);
        return new Undo(() => Scoped.Value = before);
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        private Action? undo = undo;
        public void Dispose() => Interlocked.Exchange(ref undo, null)?.Invoke();
    }

    /// <summary>How many times the git-repo check actually ran (tests use it to prove it's cached).</summary>
    internal static int RepoChecks { get => S.RepoChecks; set => S.RepoChecks = value; }
    internal static int UserNameLookups { get => S.UserNameLookups; set => S.UserNameLookups = value; }

    /// <summary>How many git processes were started (tests use it to prove a question was answered from memory).</summary>
    internal static int Processes => S.Processes;

    /// <summary>How many of the newest commits of the current branch the cached log holds; older hashes are fetched one by one.</summary>
    internal static int LogLimit { get => S.LogLimit; set => S.LogLimit = value; }
}
using GitHistory.PublicDTOs;

namespace GitHistory;

/// <summary>What is known about one repository, kept while it stays open (and across switches between open ones).</summary>
internal sealed class GitState
{
    /// <summary>Whether the root is a git work tree; checked once (each check starts a git process).</summary>
    public bool? IsRepo;

    /// <summary>git user.name, looked up once; (null, false) = not looked up yet.</summary>
    public (string? Name, bool Known) UserName;

    /// <summary>Commit authors per change name (archive date prefix dropped), read with one git log and kept until HEAD moves.</summary>
    public (string Head, Dictionary<string, List<string>> ByChange)? Authors;

    /// <summary>Every commit author in the repo, one name per person, plus each name variant → that name; kept until HEAD moves.</summary>
    public (string Head, IReadOnlyList<string> Names, Dictionary<string, string> Canonical)? People;

    /// <summary>The newest commits of the current branch, newest first, read with one git log and kept until HEAD moves.
    /// Singles holds commits looked up one by one because they are older than the log (null = git does not know the hash).</summary>
    public sealed record CommitLog(string Head, List<Queries.LogEntry> Commits, Dictionary<string, CommitInfo?> Singles);

    public CommitLog? Log;
}

/// <summary>The module's state: the configured repository, what is known about each repository, and the counters tests read.</summary>
internal sealed class GitHistoryState
{
    public string? RepoRoot;
    public readonly Dictionary<string, GitState> States = new(StringComparer.OrdinalIgnoreCase);
    public int RepoChecks, UserNameLookups, Processes;
    public int LogLimit = 5000;
}

/// <summary>Process-wide state. The app has one such state; a test runs against its own (<see cref="Isolated"/>), so tests that run at the same time never share a repository or a counter.</summary>
internal static class GitHistoryModule
{
    private static readonly GitHistoryState Shared = new();

    /// <summary>A test seam: the state of the code running from where it was set (one per test); null = the process-wide one, which is all the app ever uses.</summary>
    internal static readonly AsyncLocal<GitHistoryState?> Isolated = new();

    internal static GitHistoryState S => Isolated.Value ?? Shared;

    internal static string? RepoRoot { get => S.RepoRoot; set => S.RepoRoot = value; }
    internal static Dictionary<string, GitState> States => S.States;
    private static readonly GitState None = new();

    /// <summary>The state of the configured repository (a throwaway one when none is configured).</summary>
    internal static GitState State => RepoRoot is string r && States.TryGetValue(r, out GitState? s) ? s : None;

    /// <summary>How many times the git-repo check actually ran (tests use it to prove it's cached).</summary>
    internal static int RepoChecks { get => S.RepoChecks; set => S.RepoChecks = value; }
    internal static int UserNameLookups { get => S.UserNameLookups; set => S.UserNameLookups = value; }

    /// <summary>How many git processes were started (tests use it to prove a question was answered from memory).</summary>
    internal static int Processes => S.Processes;

    /// <summary>How many of the newest commits of the current branch the cached log holds; older hashes are fetched one by one.</summary>
    internal static int LogLimit { get => S.LogLimit; set => S.LogLimit = value; }
}
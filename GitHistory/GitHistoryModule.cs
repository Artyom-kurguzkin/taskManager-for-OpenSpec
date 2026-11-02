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
}

internal static class GitHistoryModule
{
    internal static string? RepoRoot;
    internal static readonly Dictionary<string, GitState> States = new(StringComparer.OrdinalIgnoreCase);
    private static readonly GitState None = new();

    /// <summary>The state of the configured repository (a throwaway one when none is configured).</summary>
    internal static GitState State => RepoRoot is string r && States.TryGetValue(r, out GitState? s) ? s : None;

    /// <summary>How many times the git-repo check actually ran (tests use it to prove it's cached).</summary>
    internal static int RepoChecks;
    internal static int UserNameLookups;
}
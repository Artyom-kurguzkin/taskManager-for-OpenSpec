namespace GitHistory;

internal static class GitHistoryModule
{
    internal static string? RepoRoot;

    /// <summary>Whether RepoRoot is a git work tree; checked once per Configure (each check starts a git process).</summary>
    internal static bool? IsRepo;

    /// <summary>How many times the git-repo check actually ran (tests use it to prove it's cached).</summary>
    internal static int RepoChecks;

    /// <summary>git user.name, looked up once per Configure; (null, false) = not looked up yet.</summary>
    internal static (string? Name, bool Known) UserName;
    internal static int UserNameLookups;
}

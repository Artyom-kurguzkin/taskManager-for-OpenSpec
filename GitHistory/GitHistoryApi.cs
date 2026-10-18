using GitHistory.PublicDTOs;

namespace GitHistory;

/// <summary>
/// The only public type in this assembly. Reads history through the git CLI on PATH; every read quietly returns
/// nothing when the folder is not a repository or git is missing, so the rest of the app keeps working.
/// </summary>
public static class GitHistoryApi
{
    public static void Configure(string repoRoot) => GitHistoryModule.RepoRoot = Path.GetFullPath(repoRoot);

    public static bool IsRepository() => Queries.IsRepository();
    public static string? UserName() => Queries.UserName();

    /// <summary>Commits that touched any of the repo-relative paths, newest first (pass the active and archived folder).</summary>
    public static IReadOnlyList<CommitInfo> Log(IReadOnlyList<string> paths) => Queries.Log(paths);

    /// <summary>Commits whose message contains the token as a whole word (#wi123 does not match #wi1234).</summary>
    public static IReadOnlyList<CommitInfo> Mentioning(string token) => Queries.Mentioning(token);

    /// <summary>Stages and commits only the given repo-relative paths.</summary>
    public static (bool Success, string Message) Commit(IReadOnlyList<string> paths, string message) => Commands.Commit(paths, message);
}

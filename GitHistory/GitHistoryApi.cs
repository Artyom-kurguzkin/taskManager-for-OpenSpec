using GitHistory.PublicDTOs;

namespace GitHistory;

/// <summary>
/// The only public type in this assembly. Reads history through the git CLI on PATH; every read quietly returns
/// nothing when the folder is not a repository or git is missing, so the rest of the app keeps working.
/// </summary>
public static class GitHistoryApi
{
    public static void Configure(string repoRoot)
    {
        GitHistoryModule.RepoRoot = Path.GetFullPath(repoRoot);
        GitHistoryModule.IsRepo = null;
        GitHistoryModule.UserName = (null, false);
        GitHistoryModule.Authors = null;
        GitHistoryModule.People = null;
    }

    public static bool IsRepository() => Queries.IsRepository();
    public static string? UserName() => Queries.UserName();

    /// <summary>Commits whose message contains the token as a whole word (#wi123 does not match #wi1234).</summary>
    public static IReadOnlyList<CommitInfo> Mentioning(string token) => Queries.Mentioning(token);

    /// <summary>The newest commits of the current branch, newest first (what "+ link commit" offers).</summary>
    public static IReadOnlyList<CommitInfo> Recent(int count) => Queries.Recent(count);

    /// <summary>The commits for hashes picked earlier; unknown hashes are skipped.</summary>
    public static IReadOnlyList<CommitInfo> Commits(IReadOnlyList<string> hashes) => Queries.Commits(hashes);

    /// <summary>Authors of commits that touched openspec/changes/&lt;folder&gt; (or its archived copy), newest first.</summary>
    public static IReadOnlyList<string> AuthorsOfChange(string folder) => Queries.AuthorsOfChange(folder);

    /// <summary>Everyone who has ever committed to the repository (all branches), one name per person, alphabetically.</summary>
    public static IReadOnlyList<string> AllAuthors() => Queries.AllAuthors();

    /// <summary>The name AllAuthors lists for this commit-author name variant; other names come back unchanged.</summary>
    public static string PersonName(string name) => Queries.PersonName(name);

    /// <summary>Changes whenever a commit is made (read from .git files, no process); "" when there is no commit.</summary>
    public static string HeadToken() => Queries.HeadToken();

    /// <summary>Stages and commits only the given repo-relative paths.</summary>
    public static (bool Success, string Message) Commit(IReadOnlyList<string> paths, string message) => Commands.Commit(paths, message);
}

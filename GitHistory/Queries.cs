using System.Globalization;
using System.Text.RegularExpressions;
using GitHistory.Infrastructure;
using GitHistory.PublicDTOs;

namespace GitHistory;

internal static class Queries
{
    private const string Format = "--format=%h%x1f%s%x1f%an%x1f%aI";

    /// <summary>True only when the configured folder is itself the top of a work tree (not just inside a parent repo).</summary>
    internal static bool IsRepository()
    {
        if (GitHistoryModule.IsRepo is bool known) return known;
        GitHistoryModule.RepoChecks++;
        (int exit, string output, string _) = GitCli.Run("rev-parse", "--show-toplevel");
        bool isRepo = exit == 0 && GitHistoryModule.RepoRoot is string root &&
               Path.GetFullPath(output.Trim()).TrimEnd(Path.DirectorySeparatorChar).Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        GitHistoryModule.IsRepo = isRepo;
        return isRepo;
    }

    internal static string? UserName()
    {
        if (GitHistoryModule.UserName.Known) return GitHistoryModule.UserName.Name;
        GitHistoryModule.UserNameLookups++;
        (int exit, string output, string _) = GitCli.Run("config", "user.name");
        string? name = exit == 0 && output.Trim().Length > 0 ? output.Trim() : null;
        GitHistoryModule.UserName = (name, true);
        return name;
    }

    internal static IReadOnlyList<CommitInfo> Log(IReadOnlyList<string> paths) =>
        IsRepository() ? Parse(GitCli.Run(["log", Format, "--", .. paths]).Output) : [];

    internal static IReadOnlyList<CommitInfo> Mentioning(string token)
    {
        if (!IsRepository()) return [];
        Regex whole = new($"(?<![\\w#]){Regex.Escape(token)}(?![\\w])");
        return Parse(GitCli.Run("log", Format, "--fixed-strings", $"--grep={token}").Output).Where(c => whole.IsMatch(c.Subject)).ToList();
    }

    private static List<CommitInfo> Parse(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\x1f'))
            .Where(p => p.Length == 4)
            .Select(p => new CommitInfo(p[0], p[1], p[2], DateTime.Parse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)))
            .ToList();
}

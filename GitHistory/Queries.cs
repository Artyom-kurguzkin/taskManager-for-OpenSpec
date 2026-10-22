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

    internal static IReadOnlyList<CommitInfo> Mentioning(string token)
    {
        if (!IsRepository()) return [];
        Regex whole = new($"(?<![\\w#]){Regex.Escape(token)}(?![\\w])");
        return Parse(GitCli.Run("log", Format, "--fixed-strings", $"--grep={token}").Output).Where(c => whole.IsMatch(c.Subject)).ToList();
    }

    internal static IReadOnlyList<string> AuthorsOfChange(string folder)
    {
        if (!IsRepository()) return [];
        string head = HeadToken();
        if (GitHistoryModule.Authors is not { } a || a.Head != head)
        {
            a = (head, ReadAuthors());
            GitHistoryModule.Authors = a;
        }
        return a.ByChange.TryGetValue(ChangeName(folder), out List<string>? authors) ? [.. authors.Select(PersonName).Distinct()] : [];
    }

    internal static IReadOnlyList<string> AllAuthors() => People().Names;

    internal static string PersonName(string name) => People().Canonical.GetValueOrDefault(name, name);

    /// <summary>
    /// Everyone who committed (all branches). Names equal apart from case and - _ . and emails equal apart from case are
    /// one person, shown under the name they committed with most.
    /// ponytail: unrelated names on unrelated emails (work vs personal account) can't be told apart; a .mailmap merges those.
    /// Keyed on the current branch's HEAD, so a commit on another branch shows after the next local commit or reopen.
    /// </summary>
    private static (IReadOnlyList<string> Names, Dictionary<string, string> Canonical) People()
    {
        if (!IsRepository()) return ([], []);
        string head = HeadToken();
        if (GitHistoryModule.People is { } cached && cached.Head == head) return (cached.Names, cached.Canonical);

        var rows = GitCli.Run("log", "--all", "--format=%aN%x1f%aE").Output.Split('\n')
            .Select(l => l.TrimEnd('\r').Split('\x1f')).Where(p => p.Length == 2 && p[0].Trim().Length > 0)
            .GroupBy(p => (Name: p[0].Trim(), Email: p[1].Trim().ToLowerInvariant()))
            .Select(g => (g.Key.Name, g.Key.Email, Count: g.Count()));

        List<(HashSet<string> Keys, List<(string Name, int Count)> Names)> people = [];
        foreach (var (name, email, count) in rows)
        {
            HashSet<string> keys = ["n:" + Regex.Replace(name.ToLowerInvariant(), @"[\s\-_.]+", " ").Trim()];
            if (email.Length > 0) keys.Add("e:" + email);
            (HashSet<string> Keys, List<(string, int)> Names) merged = (keys, [(name, count)]);
            foreach (var same in people.Where(p => p.Keys.Overlaps(keys)).ToList())
            {
                merged.Keys.UnionWith(same.Keys);
                merged.Names.AddRange(same.Names);
                people.Remove(same);
            }
            people.Add(merged);
        }

        Dictionary<string, string> canonical = [];
        foreach (var person in people)
        {
            string main = person.Names.GroupBy(n => n.Name).OrderByDescending(g => g.Sum(n => n.Count)).First().Key;
            foreach ((string name, int _) in person.Names) canonical[name] = main;
        }
        List<string> names = [.. canonical.Values.Distinct().Order(StringComparer.OrdinalIgnoreCase)];
        GitHistoryModule.People = (head, names, canonical);
        return (names, canonical);
    }

    // One "git log --name-only" over openspec/changes: each commit is "\x1e<author>" followed by the files it touched.
    private static Dictionary<string, List<string>> ReadAuthors()
    {
        Dictionary<string, List<string>> byChange = [];
        string? author = null;
        foreach (string raw in GitCli.Run("log", "--format=%x1e%aN", "--name-only", "--", "openspec/changes").Output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith('\x1e')) { author = line[1..]; continue; }
            string[] parts = line.Split('/');   // openspec/changes/<name>/… or openspec/changes/archive/<date-name>/…
            if (author is null || parts.Length < 4 || parts[0] != "openspec" || parts[1] != "changes") continue;
            if (parts[2] == "archive" && parts.Length < 5) continue;
            string name = ChangeName(parts[2] == "archive" ? parts[3] : parts[2]);
            List<string> list = byChange.TryGetValue(name, out List<string>? l) ? l : byChange[name] = [];
            if (!list.Contains(author)) list.Add(author);
        }
        return byChange;
    }

    /// <summary>"2026-09-18-session-timeout" (archived) and "session-timeout" are the same change.</summary>
    private static string ChangeName(string folder) => Regex.Replace(folder, @"^\d{4}-\d{2}-\d{2}-", "");

    // ponytail: reads .git/HEAD and the branch ref directly; worktrees/submodules (.git is a file) fall back to git rev-parse.
    internal static string HeadToken()
    {
        if (GitHistoryModule.RepoRoot is not string root) return "";
        string git = Path.Combine(root, ".git");
        if (!Directory.Exists(git)) return GitCli.Run("rev-parse", "HEAD").Output.Trim();
        string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
        if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head;
        string name = head[5..];
        string loose = Path.Combine(git, name);
        if (File.Exists(loose)) return File.ReadAllText(loose).Trim();
        string packed = Path.Combine(git, "packed-refs");
        return File.Exists(packed) ? File.ReadLines(packed).FirstOrDefault(l => l.EndsWith(" " + name, StringComparison.Ordinal)) ?? "" : "";
    }

    internal static IReadOnlyList<CommitInfo> Recent(int count) =>
        IsRepository() ? Parse(GitCli.Run("log", Format, $"-{count}").Output) : [];

    // One "git show" per hash: an unknown hash fails on its own instead of failing the whole list.
    internal static IReadOnlyList<CommitInfo> Commits(IReadOnlyList<string> hashes) =>
        IsRepository() ? hashes.SelectMany(h => Parse(GitCli.Run("show", "-s", Format, h).Output)).ToList() : [];

    private static List<CommitInfo> Parse(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\x1f'))
            .Where(p => p.Length == 4)
            .Select(p => new CommitInfo(p[0], p[1], p[2], DateTime.Parse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)))
            .ToList();
}

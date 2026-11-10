using System.Globalization;
using System.Text.RegularExpressions;
using GitHistory.Infrastructure;
using GitHistory.PublicDTOs;

namespace GitHistory;

internal static class Queries
{
    private const string Format = "--format=%H%x1f%h%x1f%s%x1f%an%x1f%aI";

    /// <summary>True only when the configured folder is itself the top of a work tree (not just inside a parent repo).</summary>
    internal static bool IsRepository()
    {
        if (GitHistoryModule.State.IsRepo is bool known) return known;
        GitHistoryModule.RepoChecks++;
        (int exit, string output, string _) = GitCli.Run("rev-parse", "--show-toplevel");
        bool isRepo = exit == 0 && GitHistoryModule.RepoRoot is string root &&
               Path.GetFullPath(output.Trim()).TrimEnd(Path.DirectorySeparatorChar).Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        GitHistoryModule.State.IsRepo = isRepo;
        return isRepo;
    }

    internal static bool IsIgnored(string relativePath) => IsRepository() && GitCli.Run("check-ignore", "-q", "--", relativePath).Exit == 0;

    internal static string? UserName()
    {
        if (GitHistoryModule.State.UserName.Known) return GitHistoryModule.State.UserName.Name;
        GitHistoryModule.UserNameLookups++;
        (int exit, string output, string _) = GitCli.Run("config", "user.name");
        string? name = exit == 0 && output.Trim().Length > 0 ? output.Trim() : null;
        GitHistoryModule.State.UserName = (name, true);
        return name;
    }

    internal static IReadOnlyList<CommitInfo> Mentioning(string token)
    {
        if (!IsRepository()) return [];
        Regex whole = new($"(?<![\\w#]){Regex.Escape(token)}(?![\\w])");
        return CurrentLog().Commits.Where(c => whole.IsMatch(c.Info.Subject)).Select(c => c.Info).ToList();
    }

    internal static IReadOnlyList<string> AuthorsOfChange(string folder)
    {
        if (!IsRepository()) return [];
        string head = HeadToken();
        if (GitHistoryModule.State.Authors is not { } a || a.Head != head)
        {
            a = (head, ReadAuthors());
            GitHistoryModule.State.Authors = a;
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
        if (GitHistoryModule.State.People is { } cached && cached.Head == head) return (cached.Names, cached.Canonical);

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
        GitHistoryModule.State.People = (head, names, canonical);
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

    /// <summary>
    /// The commit the current branch points at, read from .git files (no process): HEAD, then the branch's loose ref file,
    /// then packed-refs (a fetch, gc or pack-refs moves refs there). A linked work tree keeps its HEAD in its own git dir
    /// and its refs in the main one (the "commondir" file). Anything unexpected falls back to git rev-parse.
    /// </summary>
    internal static string HeadToken()
    {
        if (GitHistoryModule.RepoRoot is not string root) return "";
        try
        {
            string dotGit = Path.Combine(root, ".git");
            string gitDir = dotGit;
            if (File.Exists(dotGit))
            {
                string pointer = File.ReadAllText(dotGit).Trim();
                if (!pointer.StartsWith("gitdir: ", StringComparison.Ordinal)) return RevParseHead();
                gitDir = Path.GetFullPath(pointer[8..], root);
            }
            else if (!Directory.Exists(dotGit)) return RevParseHead();

            string common = gitDir;
            string commonFile = Path.Combine(gitDir, "commondir");
            if (File.Exists(commonFile)) common = Path.GetFullPath(File.ReadAllText(commonFile).Trim(), gitDir);

            string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head;
            string name = head[5..];
            string loose = Path.Combine(common, name);
            if (File.Exists(loose)) return File.ReadAllText(loose).Trim();
            string packed = Path.Combine(common, "packed-refs");
            if (!File.Exists(packed)) return "";
            string? line = File.ReadLines(packed).FirstOrDefault(l => l.EndsWith(" " + name, StringComparison.Ordinal));   // "<hash> <ref>"; "^<hash>" lines are tag peels
            return line is null ? "" : line[..line.IndexOf(' ')];
        }
        catch (IOException) { return RevParseHead(); }   // a ref being rewritten while we read it
        catch (UnauthorizedAccessException) { return RevParseHead(); }
    }

    private static string RevParseHead() => GitCli.Run("rev-parse", "HEAD").Output.Trim();

    /// <summary>One commit of the cached log: the full hash is kept to match picked hashes of any length.</summary>
    internal sealed record LogEntry(string Full, CommitInfo Info);

    /// <summary>
    /// The newest commits of the current branch (at most GitHistoryModule.LogLimit), from one git log per HEAD.
    /// Older commits are not in it: Commits() asks git for those one at a time. Recent and Mentioning only look at the
    /// newest LogLimit commits, which is far more than a board links to.
    /// </summary>
    private static GitState.CommitLog CurrentLog()
    {
        GitState state = GitHistoryModule.State;
        lock (state)
        {
            string head = HeadToken();
            if (state.Log is { } log && log.Head == head) return log;
            string output = GitCli.Run("log", Format, $"-{GitHistoryModule.LogLimit}").Output;
            state.Log = new GitState.CommitLog(head, ParseLog(output), []);
            return state.Log;
        }
    }

    internal static IReadOnlyList<CommitInfo> Recent(int count) =>
        IsRepository() ? CurrentLog().Commits.Take(count).Select(c => c.Info).ToList() : [];

    internal static IReadOnlyList<CommitInfo> Commits(IReadOnlyList<string> hashes)
    {
        if (!IsRepository()) return [];
        GitState.CommitLog log = CurrentLog();
        List<CommitInfo> found = [];
        foreach (string hash in hashes)
        {
            string h = hash.Trim();
            if (h.Length == 0) continue;
            LogEntry? hit = log.Commits.FirstOrDefault(c => c.Full.StartsWith(h, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) { found.Add(hit.Info); continue; }
            lock (GitHistoryModule.State)
            {
                // Older than the log (or on another branch): one "git show" per hash, so an unknown hash fails on its own; remembered until HEAD moves.
                if (!log.Singles.TryGetValue(h, out CommitInfo? single))
                    log.Singles[h] = single = ParseLog(GitCli.Run("show", "-s", Format, h).Output).FirstOrDefault()?.Info;
                if (single is not null) found.Add(single);
            }
        }
        return found;
    }

    private static List<LogEntry> ParseLog(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').Split('\x1f'))
            .Where(p => p.Length == 5)
            .Select(p => new LogEntry(p[0], new CommitInfo(p[1], p[2], p[3], DateTime.Parse(p[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))))
            .ToList();
}

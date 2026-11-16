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

    internal static IReadOnlyList<CommitInfo> Mentioning(Func<string, bool> mentions)
    {
        if (!IsRepository()) return [];
        return CurrentLog().Commits.Where(c => mentions(c.Info.Subject)).Select(c => c.Info).ToList();
    }

    internal static IReadOnlyList<string> AuthorsOfChange(string folder)
    {
        if (!IsRepository()) return [];
        return Changes().Authors.TryGetValue(ChangeName(folder), out List<string>? authors) ? [.. authors.Select(PersonName).Distinct()] : [];
    }

    internal static IReadOnlySet<string> ChangesOf(string commit)
    {
        string h = commit.Trim();
        if (h.Length == 0 || !IsRepository()) return new HashSet<string>();
        return Changes().Commits.FirstOrDefault(c => c.Full.StartsWith(h, StringComparison.OrdinalIgnoreCase))?.Names ?? (IReadOnlySet<string>)new HashSet<string>();
    }

    internal static CommitInfo? LastCommitOf(string folder) =>
        IsRepository() && Changes().Last.TryGetValue(ChangeName(folder), out CommitInfo? last) ? last : null;

    /// <summary>
    /// The changes whose files are modified, staged, deleted or new in the work tree. From the host's cache when it installed one (see GitHistoryApi.UseReads),
    /// which keeps the answer under HEAD, the index and the board's content version (or a fingerprint of the files in openspec/changes); else git is asked now.
    /// </summary>
    internal static IReadOnlySet<string> Uncommitted(long? contentVersion)
    {
        if (!IsRepository() || GitHistoryModule.RepoRoot is null) return new HashSet<string>();
        return GitHistoryModule.Reads?.Uncommitted(contentVersion) ?? ReadUncommitted();
    }

    /// <summary>Git asked now for the changes with files modified, staged, deleted or new in the work tree.</summary>
    internal static IReadOnlySet<string> ReadUncommitted()
    {
        if (!IsRepository()) return new HashSet<string>();
        HashSet<string> names = [];
        string[] entries = GitCli.Run("--no-optional-locks", "status", "--porcelain", "-z", "-uall", "--", "openspec/changes").Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);   // no optional locks: reading must not rewrite the index, which the watcher would take for a change
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Length < 4) continue;
            if (ChangeOfPath(entries[i][3..]) is string name) names.Add(name);
            if (entries[i][0] is 'R' or 'C' || entries[i][1] is 'R' or 'C')   // a rename or copy carries its source path as the next entry
                if (++i < entries.Length && ChangeOfPath(entries[i]) is string from) names.Add(from);
        }
        return names;
    }

    /// <summary>A fingerprint of the files in openspec/changes (path, size, write time: one directory walk, no process) for a cache that has no content version to go by.</summary>
    internal static long ChangesFingerprint() => GitHistoryModule.RepoRoot is string root ? Fingerprint(Path.Combine(root, "openspec", "changes")) : 0;

    private static long Fingerprint(string changes)
    {
        long sum = 0;
        GitHistoryModule.S.Walks++;
        if (!Directory.Exists(changes)) return sum;
        try
        {
            foreach (FileInfo f in new DirectoryInfo(changes).EnumerateFiles("*", SearchOption.AllDirectories))
                sum = unchecked(sum + HashCode.Combine(f.FullName, f.Length, f.LastWriteTimeUtc.Ticks));
        }
        catch (IOException) { sum = -System.Diagnostics.Stopwatch.GetTimestamp(); }   // being moved while we look (an archive): a fingerprint no later walk gives
        catch (UnauthorizedAccessException) { sum = -System.Diagnostics.Stopwatch.GetTimestamp(); }
        return sum;
    }

    /// <summary>The change a repo-relative path belongs to (openspec/changes/&lt;name&gt;/… or openspec/changes/archive/&lt;date-name&gt;/…), or null.</summary>
    private static string? ChangeOfPath(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        if (parts.Length < 4 || parts[0] != "openspec" || parts[1] != "changes") return null;
        if (parts[2] == "archive") return parts.Length < 5 ? null : ChangeName(parts[3]);
        return ChangeName(parts[2]);
    }

    internal static IReadOnlyList<string> AllAuthors() => IsRepository() ? People().Names : [];

    internal static string PersonName(string name) => IsRepository() ? People().Canonical.GetValueOrDefault(name, name) : name;

    /// <summary>
    /// Everyone who committed (all branches). Names equal apart from case and - _ . and emails equal apart from case are
    /// one person, shown under the name they committed with most.
    /// ponytail: unrelated names on unrelated emails (work vs personal account) can't be told apart; a .mailmap merges those.
    /// Keyed on the current branch's HEAD, so a commit on another branch shows after the next local commit or reopen.
    /// </summary>
    private static PeopleRead People() => GitHistoryModule.Reads?.People() ?? ReadPeople();

    /// <summary>Everyone who committed, read from git now (see <see cref="People"/>).</summary>
    internal static PeopleRead ReadPeople()
    {
        if (!IsRepository()) return new PeopleRead("", [], []);
        string head = HeadToken();

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
        return new PeopleRead(head, names, canonical);
    }

    private static ChangeLog Changes() => GitHistoryModule.Reads?.Changes() ?? ReadChanges();

    /// <summary>
    /// One "git log --name-only" over openspec/changes, read now: each commit is "\x1e" and its author, hashes, subject and date, followed by the files it touched.
    /// It gives the authors per change, the newest commit per change and the changes each commit touched.
    /// </summary>
    internal static ChangeLog ReadChanges()
    {
        string head = HeadToken();
        Dictionary<string, List<string>> authors = [];
        Dictionary<string, CommitInfo> last = [];
        List<ChangeCommit> commits = [];
        ChangeCommit? current = null;
        foreach (string raw in GitCli.Run("log", "--format=%x1e%aN%x1f%H%x1f%h%x1f%s%x1f%aI", "--name-only", "--", "openspec/changes").Output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith('\x1e'))
            {
                string[] p = line[1..].Split('\x1f');
                current = p.Length != 5 ? null : new ChangeCommit(p[1], new CommitInfo(p[2], p[3], p[0], DateTime.Parse(p[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)), []);
                if (current is not null) commits.Add(current);
                continue;
            }
            if (current is null || ChangeOfPath(line) is not string name) continue;
            current.Names.Add(name);
            List<string> list = authors.TryGetValue(name, out List<string>? l) ? l : authors[name] = [];
            if (!list.Contains(current.Info.Author)) list.Add(current.Info.Author);
            last.TryAdd(name, current.Info);   // newest first: the first commit seen for a change is its last
        }
        return new ChangeLog(head, authors, last, commits);
    }

    /// <summary>"2026-09-18-session-timeout" (archived) and "session-timeout" are the same change.</summary>
    internal static string ChangeName(string folder) => Regex.Replace(folder, @"^\d{4}-\d{2}-\d{2}-", "");

    /// <summary>
    /// The commit the current branch points at, read from .git files (no process): HEAD, then the branch's loose ref file,
    /// then packed-refs (a fetch, gc or pack-refs moves refs there). A linked work tree keeps its HEAD in its own git dir
    /// and its refs in the main one (the "commondir" file). Anything unexpected falls back to git rev-parse.
    /// </summary>
    internal static string HeadToken()
    {
        if (GitHistoryModule.RepoRoot is not string root) return "";
        return GitHistoryModule.State.Watcher is { } watcher ? watcher.Head : HeadTokenFromDisk(root);   // a watcher keeps it current: no file read per call
    }

    /// <summary>The size and write time of the index (it changes when something is staged or committed); from the watcher while one runs.</summary>
    internal static string IndexStamp()
    {
        if (GitHistoryModule.State.Watcher is { } watcher) return watcher.IndexStamp;
        if (GitHistoryModule.RepoRoot is not string root || GitDirs(root) is not { } dirs) return "";
        FileInfo f = new(Path.Combine(dirs.GitDir, "index"));
        return f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}" : "-";
    }

    /// <summary>The git directory (the .git folder, or the one a linked work tree's .git file points to) and the common one that holds refs, packed-refs and config; null when there is no .git or it is unexpected.</summary>
    internal static (string GitDir, string Common)? GitDirs(string root)
    {
        string dotGit = Path.Combine(root, ".git");
        string gitDir = dotGit;
        try
        {
            if (File.Exists(dotGit))
            {
                string pointer = File.ReadAllText(dotGit).Trim();
                if (!pointer.StartsWith("gitdir: ", StringComparison.Ordinal)) return null;
                gitDir = Path.GetFullPath(pointer[8..], root);
            }
            else if (!Directory.Exists(dotGit)) return null;

            string common = gitDir;
            string commonFile = Path.Combine(gitDir, "commondir");
            if (File.Exists(commonFile)) common = Path.GetFullPath(File.ReadAllText(commonFile).Trim(), gitDir);
            return (gitDir, common);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static string HeadTokenFromDisk(string root)
    {
        GitHistoryModule.S.HeadReads++;
        try
        {
            if (GitDirs(root) is not { } dirs) return RevParseHead();
            (string gitDir, string common) = dirs;

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

    /// <summary>
    /// The newest commits of the current branch (at most GitHistoryModule.LogLimit), from one git log per HEAD (the host's cache when it installed one).
    /// Older commits are not in it: Commits() asks git for those one at a time. Recent and Mentioning only look at the
    /// newest LogLimit commits, which is far more than a board links to.
    /// </summary>
    private static CommitLog CurrentLog() => GitHistoryModule.Reads?.Log() ?? ReadCommitLog();

    /// <summary>The newest commits of the current branch read from git now.</summary>
    internal static CommitLog ReadCommitLog() => new(HeadToken(), ParseLog(GitCli.Run("log", Format, $"-{GitHistoryModule.LogLimit}").Output));

    /// <summary>One commit looked up by hash, from git now (older than the log, or on another branch); null when git does not know the hash.</summary>
    internal static CommitInfo? ReadCommit(string hash) => ParseLog(GitCli.Run("show", "-s", Format, hash).Output).FirstOrDefault()?.Info;

    internal static IReadOnlyList<CommitInfo> Recent(int count) =>
        IsRepository() ? CurrentLog().Commits.Take(count).Select(c => c.Info).ToList() : [];

    internal static IReadOnlyList<CommitInfo> Commits(IReadOnlyList<string> hashes)
    {
        if (!IsRepository()) return [];
        CommitLog log = CurrentLog();
        List<CommitInfo> found = [];
        foreach (string hash in hashes)
        {
            string h = hash.Trim();
            if (h.Length == 0) continue;
            LogEntry? hit = log.Commits.FirstOrDefault(c => c.Full.StartsWith(h, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) { found.Add(hit.Info); continue; }
            // Older than the log (or on another branch): one "git show" per hash, so an unknown hash fails on its own.
            if ((GitHistoryModule.Reads?.Single(h) ?? ReadCommit(h)) is { } single) found.Add(single);
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

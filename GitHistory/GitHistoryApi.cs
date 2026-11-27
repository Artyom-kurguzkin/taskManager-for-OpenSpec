using GitHistory.PublicDTOs;

namespace GitHistory;

/// <summary>
/// The only public type in this assembly. Reads history through the git CLI on PATH; every read quietly returns
/// nothing when the folder is not a repository or git is missing, so the rest of the app keeps working.
/// </summary>
public static class GitHistoryApi
{
    /// <summary>Points the reads at a repository. What was learnt about it (is it git, user.name, authors) is kept while it
    /// stays configured or open as a tab; <paramref name="reset"/> forgets it (a fresh open).</summary>
    public static void Configure(string repoRoot, bool reset = false)
    {
        string root = Path.GetFullPath(repoRoot);
        Dictionary<string, GitState> states = GitHistoryModule.States;
        lock (states)
        {
            GitHistoryModule.RepoRoot = root;
            if (reset || !states.ContainsKey(root))
            {
                if (states.TryGetValue(root, out GitState? old)) old.Watcher?.Dispose();   // a fresh open: what the old state watched is gone with it
                states[root] = new GitState();
            }
        }
    }

    /// <summary>Points the reads of this call (the code running from here until the scope is disposed) at a repository, without changing the one <see cref="Configure"/> set for everyone else.</summary>
    public static IDisposable InRepo(string repoRoot) => GitHistoryModule.InRepo(repoRoot);

    /// <summary>Forgets repositories that are no longer open.</summary>
    public static void Keep(IEnumerable<string> repoRoots)
    {
        HashSet<string> keep = new(repoRoots.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, GitState> states = GitHistoryModule.States;
        lock (states)
            foreach (string root in states.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                states[root].Watcher?.Dispose();
                states.Remove(root);
            }
    }

    /// <summary>
    /// Starts watching the repository's git files (HEAD, index, packed-refs, config, refs/): a commit, checkout or fetch made outside the app moves <see cref="HeadVersion"/> and raises <see cref="Changed"/>,
    /// and HEAD and the uncommitted set are then read from what the watcher knows instead of from disk on every call. Does nothing for a folder with no .git.
    /// </summary>
    public static void Watch(string repoRoot) => GitHistoryModule.Watch(repoRoot);

    /// <summary>Stops watching the repository (it stays configured).</summary>
    public static void Unwatch(string repoRoot) => GitHistoryModule.Unwatch(repoRoot);

    /// <summary>Raised (on a background thread) when the repository's git files changed outside the app; the host re-reads what it shows.</summary>
    public static event Action? Changed
    {
        add => GitHistoryModule.S.Changed += value;
        remove => GitHistoryModule.S.Changed -= value;
    }

    /// <summary>Tells the listeners of <see cref="Changed"/> that what git says changed, for the host's own refresh of a cached answer (the uncommitted set read in the background).</summary>
    public static void RaiseChanged() => GitHistoryModule.S.RaiseChanged();

    /// <summary>Moves (never back) each time the repository's git files changed since <see cref="Watch"/>; 0 while nothing changed or nothing is watched. A cached answer about git is valid while it stays the same.</summary>
    public static long HeadVersion(string repoRoot)
    {
        Dictionary<string, GitState> states = GitHistoryModule.States;
        lock (states) return states.TryGetValue(Path.GetFullPath(repoRoot), out GitState? s) && s.Watcher is { } w ? Interlocked.Read(ref w.Version) : 0;
    }

    // ── What git says about the history, as the host's cache reads it (the module keeps no cache of its own) ──
    /// <summary>Hands the module the host's cached reads of the history for <see cref="Mentioning"/>, <see cref="Recent"/>, <see cref="AuthorsOfChange"/> and the other history reads; null = ask git on every call.</summary>
    public static void UseReads(GitReads? reads) => GitHistoryModule.Reads = reads;

    /// <summary>The repository the reads of this call go to (the one <see cref="InRepo"/> scoped it to, else the configured one); null when none.</summary>
    public static string? RepoRoot => GitHistoryModule.RepoRoot;

    /// <summary>Different for each time the repository was configured afresh (a fresh open): what git said before it is not valid after.</summary>
    public static long Session(string repoRoot)
    {
        Dictionary<string, GitState> states = GitHistoryModule.States;
        lock (states) return states.TryGetValue(Path.GetFullPath(repoRoot), out GitState? s) ? s.Session : 0;
    }

    /// <summary>One git log over the board's changes folder, read now: authors per change, the newest commit per change and the changes each commit touched.</summary>
    public static ChangeLog ReadChanges() => Queries.ReadChanges();

    /// <summary>The newest commits of the current branch, read now with one git log.</summary>
    public static CommitLog ReadCommitLog() => Queries.ReadCommitLog();

    /// <summary>One commit by hash, read now; null when git does not know it.</summary>
    public static CommitInfo? ReadCommit(string hash) => Queries.ReadCommit(hash);

    /// <summary>Every commit author (all branches) with the canonical name of each variant, read now.</summary>
    public static PeopleRead ReadPeople() => Queries.ReadPeople();

    /// <summary>The changes with files modified, staged, deleted or new in the work tree, asked of git now.</summary>
    public static IReadOnlySet<string> ReadUncommitted() => Queries.ReadUncommitted();

    /// <summary>The size and write time of git's index (it changes when something is staged or committed); from the watcher while one runs.</summary>
    public static string IndexStamp() => Queries.IndexStamp();

    /// <summary>A fingerprint of the files in the board's changes folder (one directory walk, no process), for a cache that has no content version to go by.</summary>
    public static long ChangesFingerprint() => Queries.ChangesFingerprint();

    public static bool IsRepository() => Queries.IsRepository();
    public static string? UserName() => Queries.UserName();

    /// <summary>Whether git would ignore this repo-relative path, by any rule source (.gitignore files, info/exclude, global excludes); false when not a repository.</summary>
    public static bool IsIgnored(string relativePath) => Queries.IsIgnored(relativePath);

    /// <summary>Commits whose subject <paramref name="mentions"/> says yes to. What counts as a mention (#wi123 is not #wi1234) is the board's reference grammar, which this module does not know: the host hands it in (OpenSpecStoreApi.MentionsItem).</summary>
    public static IReadOnlyList<CommitInfo> Mentioning(Func<string, bool> mentions) => Queries.Mentioning(mentions);

    /// <summary>The newest commits of the current branch, newest first (what "+ link commit" offers).</summary>
    public static IReadOnlyList<CommitInfo> Recent(int count) => Queries.Recent(count);

    /// <summary>The commits for hashes picked earlier; unknown hashes are skipped.</summary>
    public static IReadOnlyList<CommitInfo> Commits(IReadOnlyList<string> hashes) => Queries.Commits(hashes);

    /// <summary>Authors of commits that touched openspec/changes/&lt;folder&gt; (or its archived copy), newest first.</summary>
    public static IReadOnlyList<string> AuthorsOfChange(string folder) => Queries.AuthorsOfChange(folder);

    /// <summary>The names of the changes (work item folders, archive date prefix dropped) whose files the commit changed; empty for a commit that touched none, or an unknown hash (short or full).</summary>
    public static IReadOnlySet<string> ChangesOf(string commit) => Queries.ChangesOf(commit);

    /// <summary>The names of the changes with files modified, staged, deleted or new in the work tree: the work that is not committed yet.</summary>
    public static IReadOnlySet<string> Uncommitted() => Queries.Uncommitted(null);

    /// <summary>
    /// The same, recomputed only when git's files moved (<see cref="HeadVersion"/>, the index) or <paramref name="contentVersion"/> did: pass the board's content version (what moves when a file of the board is saved).
    /// Unlike <see cref="Uncommitted()"/> it does not walk the changes folder to find out.
    /// </summary>
    public static IReadOnlySet<string> Uncommitted(long contentVersion) => Queries.Uncommitted(contentVersion);

    /// <summary>The newest commit that touched openspec/changes/&lt;folder&gt; (or its archived copy); null when none did.</summary>
    public static CommitInfo? LastCommitOf(string folder) => Queries.LastCommitOf(folder);

    /// <summary>"2026-09-18-session-timeout" (archived) and "session-timeout" are the same change: the name without the archive date prefix.</summary>
    public static string ChangeName(string folder) => Queries.ChangeName(folder);

    /// <summary>Everyone who has ever committed to the repository (all branches), one name per person, alphabetically.</summary>
    public static IReadOnlyList<string> AllAuthors() => Queries.AllAuthors();

    /// <summary>The name AllAuthors lists for this commit-author name variant; other names come back unchanged.</summary>
    public static string PersonName(string name) => Queries.PersonName(name);

    /// <summary>Changes whenever a commit is made (read from .git files, no process); "" when there is no commit.</summary>
    public static string HeadToken() => Queries.HeadToken();

    /// <summary>
    /// The sides of a board file that a git merge left conflict markers in, with the text of the file as it is now (<paramref name="markedText"/>): taken from the open merge (HEAD and MERGE_HEAD) or from the merge commit that committed the markers
    /// (its two parents), with their merge base as the common ancestor. null when this is not a repository, another operation is open (rebase, cherry-pick, revert), the merge has more than two sides, or the markers did not come from one merge commit.
    /// </summary>
    public static ConflictSides? ConflictSides(string repoRelativePath, string markedText) => ConflictSidesReader.Read(repoRelativePath, markedText);

    /// <summary>The hash of the repository's first commit (the same on every clone, so it names the project); the oldest when there are several roots, by hash order for a tie. null when this is not a repository or it has no commit.</summary>
    public static string? FirstCommit() => Queries.FirstCommit();

    /// <summary>The text of a repo-relative file as the last commit (HEAD) has it; null when there is none (no repository, no commit, not committed). Read only.</summary>
    public static string? CommittedText(string repoRelativePath) => Queries.CommittedText(repoRelativePath);

    /// <summary>Stages and commits only the given repo-relative paths.</summary>
    public static (bool Success, string Message) Commit(IReadOnlyList<string> paths, string message) => Commands.Commit(paths, message);
}

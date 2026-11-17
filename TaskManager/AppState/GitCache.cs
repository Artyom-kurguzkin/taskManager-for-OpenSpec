using GitHistory;
using GitHistory.PublicDTOs;

namespace TaskManager.AppState;

/// <summary>
/// What git says about the history of each open repository, asked of git again only when HEAD moves (a commit or a checkout, inside the app or out: GitHistory's watcher
/// moves the head token) or the repository was opened afresh. The module (GitHistory) reads and parses; what is kept, and until when, is decided here.
/// The answers to the host's questions (<see cref="LastCommitOf"/>, <see cref="AuthorsOfChange"/>, <see cref="Mentioning"/>, ...) are the module's own lookups over what is kept.
/// </summary>
public static class GitCache
{
    internal sealed class Entry
    {
        internal long Session;
        internal (string Head, ChangeLog Value)? Changes;
        internal (string Head, CommitLog Value)? Log;
        internal readonly Dictionary<int, (string Head, IReadOnlyList<CommitInfo> Commits)> Mentions = [];   // per work item: its commits, until HEAD moves
        internal readonly Dictionary<string, CommitInfo?> Singles = [];   // older commits looked up one by one, kept until HEAD moves (with the log they are older than)
        internal (string Head, PeopleRead Value)? People;
        internal (string Head, long Fingerprint, IReadOnlySet<string> Names)? Dirty;
        internal (string Head, long Fingerprint)? Wanted;   // what the running refresh still has to catch up with
        internal bool Refreshing;
    }

    internal sealed class State
    {
        public readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
        public readonly object Gate = new();
        public int Reads;

        /// <summary>Where a refresh of the uncommitted set runs: the thread pool; a test queues it instead and runs it when it chooses.</summary>
        public Func<Action, Task> Background = work => Task.Run(work);
    }

    /// <summary>How many times git was asked for a history, a log, authors or the uncommitted set (a test counts them).</summary>
    public static int Reads => Caches.S.Git.Reads;

    private static Entry EntryOf(State s)
    {
        string root = GitHistoryApi.RepoRoot ?? "";
        long session = root.Length == 0 ? 0 : GitHistoryApi.Session(root);   // a repository opened afresh is another session: nothing read before is served
        if (!s.Entries.TryGetValue(root, out Entry? e) || e.Session != session) s.Entries[root] = e = new Entry { Session = session };
        return e;
    }

    /// <summary>The changes folder's history (authors, newest commit and touched changes), read again when HEAD moved.</summary>
    public static ChangeLog Changes()
    {
        State s = Caches.S.Git;
        lock (s.Gate)
        {
            Entry e = EntryOf(s);
            string head = GitHistoryApi.HeadToken();
            if (e.Changes is { } known && known.Head == head) return known.Value;
            ChangeLog read = GitHistoryApi.ReadChanges();
            e.Changes = (head, read);
            s.Reads++;
            return read;
        }
    }

    /// <summary>The newest commits of the current branch, read again when HEAD moved.</summary>
    public static CommitLog Log()
    {
        State s = Caches.S.Git;
        lock (s.Gate)
        {
            Entry e = EntryOf(s);
            string head = GitHistoryApi.HeadToken();
            if (e.Log is { } known && known.Head == head) return known.Value;
            CommitLog read = GitHistoryApi.ReadCommitLog();
            e.Log = (head, read);
            e.Singles.Clear();
            s.Reads++;
            return read;
        }
    }

    /// <summary>A commit older than the log, by hash (null = git does not know it); kept until HEAD moves.</summary>
    public static CommitInfo? Single(string hash)
    {
        State s = Caches.S.Git;
        Log();   // the singles belong to the log's HEAD
        lock (s.Gate)
        {
            Entry e = EntryOf(s);
            if (!e.Singles.TryGetValue(hash, out CommitInfo? single)) { e.Singles[hash] = single = GitHistoryApi.ReadCommit(hash); s.Reads++; }
            return single;
        }
    }

    /// <summary>Every commit author with the canonical name of each variant, read again when HEAD moved.</summary>
    public static PeopleRead People()
    {
        State s = Caches.S.Git;
        lock (s.Gate)
        {
            Entry e = EntryOf(s);
            string head = GitHistoryApi.HeadToken();
            if (e.People is { } known && known.Head == head) return known.Value;
            PeopleRead read = GitHistoryApi.ReadPeople();
            e.People = (head, read);
            s.Reads++;
            return read;
        }
    }

    /// <summary>
    /// The changes with files modified, staged, deleted or new in the work tree. Saving a file does not move HEAD, so the answer is kept under HEAD, the index and the board's content version
    /// (<paramref name="contentVersion"/>), or without one under a fingerprint of the files in the changes folder. The first read of a repository asks git now; after that a moved stamp
    /// returns the set known so far at once and asks git on the thread pool, and <see cref="GitHistoryApi.Changed"/> is raised when the answer differs, so the dots follow through the normal reload.
    /// </summary>
    // ponytail: a stale set shows for as long as one `git status` takes (tens to hundreds of ms); ceiling is a dirty dot that lags its write by that much, and one pool thread per repository at a time.
    public static IReadOnlySet<string> Uncommitted(long? contentVersion)
    {
        State s = Caches.S.Git;
        lock (s.Gate)
        {
            Entry e = EntryOf(s);
            string root = GitHistoryApi.RepoRoot ?? "";
            string head = contentVersion is null ? GitHistoryApi.HeadToken() : $"v:{GitHistoryApi.HeadToken()}|{GitHistoryApi.IndexStamp()}|{(root.Length == 0 ? 0 : GitHistoryApi.HeadVersion(root))}";
            long fingerprint = contentVersion ?? GitHistoryApi.ChangesFingerprint();
            if (e.Dirty is not { } cached)
            {
                IReadOnlySet<string> first = GitHistoryApi.ReadUncommitted();
                e.Dirty = (head, fingerprint, first);
                s.Reads++;
                return first;
            }
            if (cached.Head == head && cached.Fingerprint == fingerprint) return cached.Names;
            e.Wanted = (head, fingerprint);
            if (!e.Refreshing)
            {
                e.Refreshing = true;
                s.Background(() => RefreshDirty(s, e));
            }
            return e.Dirty!.Value.Names;   // the set known so far; a background that already ran (a test's) has made it the new one
        }
    }

    /// <summary>Asks git for the uncommitted set until it has caught up with the last stamp asked for; raises GitHistory's Changed when the set differs from the one that was shown.</summary>
    private static void RefreshDirty(State s, Entry e)
    {
        bool finished = false;
        try
        {
            while (!finished)
            {
                (string Head, long Fingerprint) want;
                lock (s.Gate) want = e.Wanted!.Value;
                IReadOnlySet<string> names = GitHistoryApi.ReadUncommitted();   // outside the lock: git blocks nobody's cache hit
                bool changed, live;
                lock (s.Gate)
                {
                    IReadOnlySet<string>? before = e.Dirty?.Names;
                    changed = before is null || !before.SetEquals(names);
                    e.Dirty = (want.Head, want.Fingerprint, changed ? names : before!);
                    s.Reads++;
                    live = s.Entries.ContainsValue(e);   // a repository opened afresh meanwhile has its own entry: nothing to tell anybody
                    if (e.Wanted == want) { e.Wanted = null; e.Refreshing = false; finished = true; }
                }
                if (changed && live) GitHistoryApi.RaiseChanged();
            }
        }
        finally { if (!finished) lock (s.Gate) e.Refreshing = false; }   // git failed to run: the next call asks again
    }

    /// <summary>The names of the changes with files not committed yet; recomputed only when git's files or the board's content version moved.</summary>
    public static IReadOnlySet<string> Uncommitted() => Uncommitted(OpenSpecStore.OpenSpecStoreApi.Version);

    public static CommitInfo? LastCommitOf(string folder) => GitHistoryApi.LastCommitOf(folder);

    public static IReadOnlyList<string> AuthorsOfChange(string folder) => GitHistoryApi.AuthorsOfChange(folder);

    public static IReadOnlySet<string> ChangesOf(string commit) => GitHistoryApi.ChangesOf(commit);

    public static IReadOnlyList<CommitInfo> Recent(int count) => GitHistoryApi.Recent(count);

    public static IReadOnlyList<CommitInfo> Commits(IReadOnlyList<string> hashes) => GitHistoryApi.Commits(hashes);

    /// <summary>The commits whose subject <paramref name="mentions"/> says yes to, from the cached log.</summary>
    public static IReadOnlyList<CommitInfo> Mentioning(Func<string, bool> mentions) => GitHistoryApi.Mentioning(mentions);

    /// <summary>The commits that mention work item <paramref name="n"/> (#wi123 is not #wi1234), kept per item while HEAD stays where it was.</summary>
    public static IReadOnlyList<CommitInfo> Mentioning(int n)
    {
        State s = Caches.S.Git;
        if (!GitHistoryApi.IsRepository()) return [];   // no .git to read the head from: HeadToken would start git, on every read, for nothing
        lock (s.Gate)
        {
            Entry e = EntryOf(s);
            string head = GitHistoryApi.HeadToken();
            if (e.Mentions.TryGetValue(n, out var hit) && hit.Head == head) return hit.Commits;
            IReadOnlyList<CommitInfo> found = GitHistoryApi.Mentioning(OpenSpecStore.OpenSpecStoreApi.MentionsItem(n));
            e.Mentions[n] = (head, found);
            return found;
        }
    }

    public static IReadOnlyList<string> AllAuthors() => GitHistoryApi.AllAuthors();

    public static string PersonName(string name) => GitHistoryApi.PersonName(name);

    public static bool IsRepository() => GitHistoryApi.IsRepository();

    /// <summary>Forgets what was read of a repository (all of them without a root): the next read goes to git.</summary>
    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.Git;
        lock (s.Gate) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(Path.GetFullPath(root)); }
    }
}

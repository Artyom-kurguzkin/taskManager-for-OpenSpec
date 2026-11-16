using LinkedTestRuns;
using LinkedTestRuns.PublicDTOs;
using OpenSpecStore;

namespace TaskManager.AppState;

/// <summary>
/// The newest test results of each open repository. The board asks once per card, so the folder scan is reused for two seconds
/// (on this cache's clock) while the content version and the repository session stay; the results file itself is parsed again only when it is another file or was written again.
/// </summary>
public static class TestResultsCache
{
    internal sealed class State
    {
        public readonly Dictionary<string, (long Generation, long Version, string? File, DateTime Written, DateTimeOffset Checked, TestRun Run)> Entries = new(StringComparer.OrdinalIgnoreCase);
        public TimeProvider Clock = TimeProvider.System;
        public int Scans, Parses;
    }

    /// <summary>The clock the two-second reuse of the folder scan runs on; a test sets a fake one (null restores the system's).</summary>
    public static TimeProvider Clock { get => Caches.S.TestResults.Clock; set => Caches.S.TestResults.Clock = value ?? TimeProvider.System; }

    /// <summary>How many times the folders were scanned for the newest file, and how many times a results file was parsed (a test counts them).</summary>
    public static int Scans => Caches.S.TestResults.Scans;
    public static int Parses => Caches.S.TestResults.Parses;

    public static TestRun Get()
    {
        State s = Caches.S.TestResults;
        long version = OpenSpecStoreApi.Version;
        string? root = OpenSpecStoreApi.RepoRoot;
        long generation = OpenSpecStoreApi.RepoGeneration;
        TimeProvider clock = s.Clock;
        bool cached;
        (long Generation, long Version, string? File, DateTime Written, DateTimeOffset Checked, TestRun Run) c;
        lock (s.Entries) cached = s.Entries.TryGetValue(root ?? "", out c) && c.Generation == generation;
        if (cached && c.Version == version && clock.GetUtcNow() - c.Checked < TimeSpan.FromSeconds(2)) return c.Run;

        (string? file, DateTime written, IReadOnlyList<string> globs) = LinkedTestRunsApi.Newest();
        TestRun run;
        if (cached && c.File == file && c.Written == written) run = c.Run.Globs.SequenceEqual(globs) ? c.Run : c.Run with { Globs = globs };
        else
        {
            run = Same.Keep(cached ? c.Run : null, LinkedTestRunsApi.Parse(file, globs));
            lock (s.Entries) s.Parses++;
        }
        lock (s.Entries) { s.Entries[root ?? ""] = (generation, version, file, written, clock.GetUtcNow(), run); s.Scans++; }
        return run;
    }

    public static void Invalidate(string? root = null)
    {
        State s = Caches.S.TestResults;
        lock (s.Entries) { if (root is null) s.Entries.Clear(); else s.Entries.Remove(root); }
    }
}

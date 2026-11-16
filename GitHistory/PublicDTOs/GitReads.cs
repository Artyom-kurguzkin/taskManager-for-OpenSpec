namespace GitHistory.PublicDTOs;

/// <summary>What one "git log --name-only" over openspec/changes says, as of the HEAD it was read at: commit authors per change name (archive date prefix dropped), the newest commit per change, and the changes each commit touched (newest commit first).</summary>
public sealed record ChangeLog(string Head, Dictionary<string, List<string>> Authors, Dictionary<string, CommitInfo> Last, List<ChangeCommit> Commits);

/// <summary>One commit of the change log: the full hash, its short form with subject and date, and the names of the changes it touched.</summary>
public sealed record ChangeCommit(string Full, CommitInfo Info, HashSet<string> Names);

/// <summary>One commit of the log: the full hash is kept to match picked hashes of any length.</summary>
public sealed record LogEntry(string Full, CommitInfo Info);

/// <summary>The newest commits of the current branch, newest first, as of the HEAD they were read at.</summary>
public sealed record CommitLog(string Head, List<LogEntry> Commits);

/// <summary>Every commit author in the repo, one name per person, plus each name variant to that name, as of the HEAD they were read at.</summary>
public sealed record PeopleRead(string Head, IReadOnlyList<string> Names, Dictionary<string, string> Canonical);

/// <summary>The host's cached reads (see GitHistoryApi.UseReads): each answers from memory while HEAD (and, for the uncommitted set, the index and the content version) stays the same.</summary>
public sealed record GitReads(
    Func<ChangeLog> Changes,
    Func<CommitLog> Log,
    Func<string, CommitInfo?> Single,
    Func<PeopleRead> People,
    Func<long?, IReadOnlySet<string>> Uncommitted);

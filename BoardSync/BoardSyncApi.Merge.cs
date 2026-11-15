using BoardSync.Merge;
using BoardSync.PublicDTOs;

namespace BoardSync;

public static partial class BoardSyncApi
{
    /// <summary>
    /// Three-way merge of one board file, text in and text out (no file or network access). <paramref name="path"/> is the board-relative path with forward slashes and picks the rule
    /// (events.log, .openspec.yaml, tasks.md, a note, any other .md, anything else); <paramref name="baseText"/> is the last-synced copy, null when there is none;
    /// <paramref name="remoteIsNewer"/> says which side wins a place where both changed the same thing, and that place is returned in <see cref="MergeResult.Conflicts"/>.
    /// </summary>
    public static MergeResult Merge(string path, string? baseText, string local, string remote, bool remoteIsNewer) =>
        FileMerge.Run(path, baseText, local, remote, remoteIsNewer);

    /// <summary>
    /// The board files (paths relative to <paramref name="localRoot"/>, forward slashes, ordinal order) that still hold git conflict markers (opener, divider and closer, outside fenced code blocks), exactly the ones that pause a sync:
    /// with <paramref name="stateFolder"/> (the sync's state folder) files that are byte-identical to what the last sync wrote are left out.
    /// </summary>
    public static IReadOnlyList<string> MarkedFiles(string localRoot, string? stateFolder = null)
    {
        if (string.IsNullOrWhiteSpace(stateFolder)) return BoardSync.Sync.GitGuard.ConflictedFiles(localRoot, null);
        BoardSync.Sync.SyncStateStore store = new(stateFolder);
        BoardSync.Sync.StatCache stat = BoardSync.Sync.StatCache.Open(store, DateTime.UtcNow);        // the cache the sync pass keeps: a file unchanged in size and time is not read
        List<string> marked = BoardSync.Sync.GitGuard.ConflictedFiles(localRoot, store.Load()?.Entries, stat);
        stat.Save();
        return marked;
    }

    /// <summary>Whether <see cref="RepairMarkers"/> knows the kind of file this board-relative path is (.openspec.yaml, tasks.md, a note or events.log).</summary>
    public static bool CanRepairMarkers(string path) => FileMerge.HasFieldRule(path);

    /// <summary>
    /// Rebuilds a board file that a git merge left conflict markers in, text in and text out. <paramref name="path"/> is board-relative and picks the merge rule (.openspec.yaml, tasks.md, a note or events.log; any other file gives null).
    /// <paramref name="oursText"/> and <paramref name="theirsText"/> are the two sides and <paramref name="baseText"/> their common ancestor (null = the file did not exist there); when either side is null the hunks inside
    /// <paramref name="markedText"/> are the sides instead and ours wins what both changed. Otherwise the same merge as <see cref="Merge"/> runs and the side that <paramref name="theirsIsNewer"/> names wins what both changed;
    /// each such place is in <see cref="MarkerRepair.Conflicts"/>. null = leave the file alone: no rule for it, the sides cannot be read, or the result still has markers or (for a yaml file) is not valid yaml.
    /// </summary>
    public static MarkerRepair? RepairMarkers(string path, string markedText, string? baseText, string? oursText, string? theirsText, bool theirsIsNewer) =>
        MarkerRepairer.Run(path, markedText, baseText, oursText, theirsText, theirsIsNewer);
}

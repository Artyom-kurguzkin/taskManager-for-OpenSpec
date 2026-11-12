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
}

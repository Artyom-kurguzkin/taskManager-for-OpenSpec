namespace GitHistory.PublicDTOs;

/// <summary>
/// The three versions of a file that a git merge combined: the common ancestor (<paramref name="Base"/>, empty when the file did not exist there or the histories share nothing),
/// the version of the side the merge was made on (<paramref name="Ours"/>) and the one that was merged in (<paramref name="Theirs"/>); a side that has no such file is empty.
/// <paramref name="TheirsIsNewer"/>: the commit of the merged-in side has the later committer date. <paramref name="MergeInProgress"/>: the merge is still open (HEAD and MERGE_HEAD) and not yet committed.
/// </summary>
public sealed record ConflictSides(string Base, string Ours, string Theirs, bool TheirsIsNewer, bool MergeInProgress);

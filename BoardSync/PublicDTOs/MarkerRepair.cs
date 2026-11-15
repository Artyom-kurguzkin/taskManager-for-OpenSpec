namespace BoardSync.PublicDTOs;

/// <summary>A board file rebuilt from the sides of a conflict: the new text and the places where both sides changed the same thing and the older one lost. <paramref name="FromGit"/>: the sides came from git; false = from the hunks in the file itself.</summary>
public sealed record MarkerRepair(string Text, IReadOnlyList<MergeConflict> Conflicts, bool FromGit);

namespace BoardSync.PublicDTOs;

/// <summary>The merged text of a file and the places where both sides changed the same thing and one had to lose.</summary>
public sealed record MergeResult(string Text, IReadOnlyList<MergeConflict> Conflicts);

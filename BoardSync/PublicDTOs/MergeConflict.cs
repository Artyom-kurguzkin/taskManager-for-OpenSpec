namespace BoardSync.PublicDTOs;

/// <summary>One place where both computers changed the same thing: what it was (a key, a subtask, a stretch of text), the version kept and the one dropped.</summary>
public sealed record MergeConflict(string What, string Kept, string Dropped);

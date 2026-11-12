namespace BoardSync.PublicDTOs;

/// <summary>One place where both computers changed the same thing: the file, what it was, the version kept and the one dropped (the text of an edit that lost to a delete). <paramref name="DroppedHere"/>: true when the dropped version was this computer's, false when it was another computer's (the storage's).</summary>
public sealed record SyncConflict(string Path, string What, string Kept, string Dropped, bool DroppedHere = false);

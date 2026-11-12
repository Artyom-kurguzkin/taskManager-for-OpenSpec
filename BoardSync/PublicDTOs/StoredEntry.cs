namespace BoardSync.PublicDTOs;

/// <summary>One file in a listing: its path relative to the storage and its version.</summary>
public sealed record StoredEntry(string Path, string Version);

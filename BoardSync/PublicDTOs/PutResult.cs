namespace BoardSync.PublicDTOs;

/// <summary>Written = false: someone else wrote first, or the file was already there. Version is the new file's version when written.</summary>
public sealed record PutResult(bool Written, string? Version);

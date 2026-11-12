namespace Notes.PublicDTOs;

/// <summary>
/// What <see cref="NotesApi.MoveAttachments"/> did. Moved counts the files that arrived in the new folder (Renamed of them under a suffixed name,
/// because another file was already there); Duplicates the files dropped because the same bytes were already there; Left the links and the files that
/// could not be moved and stayed; ReferencesRewritten the attachments entries and the link targets that now name the new place.
/// </summary>
public sealed record AttachmentsMoveResult(bool Success, string Message, int Moved = 0, int Renamed = 0, int Duplicates = 0, int Left = 0, int ReferencesRewritten = 0);

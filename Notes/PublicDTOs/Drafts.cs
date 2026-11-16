namespace Notes.PublicDTOs;

/// <summary>A note as it is written by an agent (or any caller with only text): untitled, its title is the first line; Kind is "decision", "bug" or none.</summary>
public sealed record NoteDraft(
    string Body, string? Title = null, IReadOnlyList<string>? Links = null, IReadOnlyList<string>? Commits = null, IReadOnlyList<string>? Attachments = null, string? Kind = null);

/// <summary>What <see cref="NotesApi.Write"/> made. Success false: Message says why (NoteId is 0 when no note was made). Path is repo-relative.</summary>
public sealed record WrittenNote(
    bool Success, string Message, int NoteId, int WorkItemId, string Path, IReadOnlyList<string> Warnings, IReadOnlyList<string> SubtasksCreated, string Revision, string? Kind);

/// <summary>Hidden false: the item could not be hidden (Message says why) and nothing else was done. Else the decision note is in Note, which may itself be unsuccessful.</summary>
public sealed record ResolvedItem(bool Hidden, string Message, WrittenNote? Note);

/// <summary>
/// One entry of a work item's worklog. An event has its kind and text (EventIndex is its place in events.log); a note has Kind "note", its title as Text and Note set.
/// Order is the entry's place in the feed oldest first, events before notes: ties in time are broken by it, the later first.
/// </summary>
public sealed record WorklogEntry(DateTime At, string By, string Kind, string Text, Note? Note, int EventIndex, int Order);

/// <summary>A work item that took a new number because two carried one.</summary>
public sealed record RenumberedItem(int OldId, int NewId, string Folder);

/// <summary>A note that took a new number because two carried one; Path is its new file.</summary>
public sealed record RenumberedNote(int OldId, int NewId, string Path);

/// <summary>Everything a repair of duplicate numbers changed.</summary>
public sealed record DuplicateRepairs(IReadOnlyList<RenumberedItem> Items, IReadOnlyList<RenumberedNote> Notes);

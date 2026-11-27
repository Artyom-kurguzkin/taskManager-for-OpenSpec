namespace BoardSync.PublicDTOs;

/// <summary>
/// One thing the sync did on its own that the item it concerns should say (the host writes it as a `sync` event on that item's events.log).
/// <paramref name="Folder"/> is the change folder as it is after the sync (relative to the synced directory, forward slashes: where the event goes; empty for an attachment, which names only its item),
/// <paramref name="ItemId"/> its x-rtm.id when known. <paramref name="Kind"/> is one of: <c>put-back-folder</c> (a folder nothing explained was brought back),
/// <c>put-back-file</c>, <c>followed-archive</c>, <c>followed-rename</c>, <c>followed-merge</c> (the folder was moved, here or in the storage, to where another computer put it),
/// <c>carried</c> (edits made here went into the archived or merged item) and <c>noted</c> (a note was written in the item naming what happened elsewhere).
/// <paramref name="Text"/> is the sentence to write after `sync |`.
/// </summary>
public sealed record SyncNote(int? ItemId, string Folder, string Kind, string Text);

# Proposal

## Why

Seen while verifying #wi233 in the real app (2026-11-26): computer A merged two items and was closed while 12 of the merged folder's 20 files were still inside the 10-second settle window. Sync now had said "Nothing new" although files were deferred. The storage then held only 8 notes of the merged folder, with no .openspec.yaml, events.log or tasks.md. Computer B downloaded that partial folder; its app's duplicate-note-id rule renumbered the notes of the still-present source item, twice, and the sync failed with "events.log changed while it was being synced". The storage ended with the renumbered copies live.

## What Changes

Owner's direction (2026-11-27): warn, and let the next app repair.

- **Warn before it happens.** Closing the app (or pressing Sync now) while board files are still settling shows a popup: which files are waiting and that the storage would get a partial folder; the user can wait or close anyway. The state line names the deferred files instead of "Nothing new".
- **Repair when it happened anyway.** The next app instance that reaches the storage detects an incomplete change folder (a folder with notes or specs but no `.openspec.yaml`, or `mergedFrom` naming sources that are still live) and finishes the move itself: if it is the computer that made the merge, it uploads the rest; otherwise it holds the partial folder back (not downloaded, not counted) until the folder is complete, and does not renumber the sources' notes.
- A folder whose files are still settling is uploaded as a unit or not at all.
- Evidence: the #wi233 verification notes (evidence-s1-first-attempt, since removed with the worktree; the feed on #wi233 describes it).

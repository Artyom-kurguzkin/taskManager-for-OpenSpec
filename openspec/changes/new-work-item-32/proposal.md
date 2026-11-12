# Proposal

## Why

Seeing updated tasks on another computer takes a commit and a pull today. The board should sync between computers in near real time through a connected storage, chosen per project, while it stays in git for agents and history.

## What Changes

- Settings, per project, "Board storage": this folder only (default), or connected storage with a provider, its address, a key, "Test connection" and the sync state. The address is kept in the project's settings; the key stays on each computer.
- A provider contract (get, get if changed, put if unchanged, delete, list) so more storages (SharePoint, Azure Blob, Google Drive) can be added later. First providers: S3-compatible (Cloudflare R2) and a plain folder.
- Sync runs about 2 seconds after a change, on a 30 to 60 second check, on start, and when git moved (pull, branch change).
- Conflicts are merged per kind of data against the last synced state: fields, lists, subtasks, text, notes, the event log, deletes. The losing side is never dropped silently: it becomes an event or a conflict note.
- Git and sync together: old content that a checkout brings back is not uploaded; sync pauses during a git merge; event logs union-merge in git; one board per project whatever the branch.
- Work item and note numbers stay; each computer reserves a block of numbers while online.

Research, sources and the full design are in the linked plan.

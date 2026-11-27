## ADDED Requirements

### Requirement: A settling change folder is uploaded whole

While any file of a change folder is still inside the settle window, the sync SHALL hold the whole folder back: none of its files is uploaded, downloaded or removed in that pass, and the state line SHALL say how many files are waiting and in which folder instead of "Nothing new".

#### Scenario: A merge made a moment ago

- **WHEN** a merge wrote twenty files and twelve of them are still settling
- **THEN** the sync uploads none of the twenty and the state line reads "Waiting for 12 files to settle (folder <merged folder>)"

### Requirement: Closing while files settle warns first

When the app is closed while board files are still settling, it SHALL show a dialog naming the files and their folder, with "Wait and sync" (wait for the files to settle, sync once, then close; at most five minutes) and "Close anyway" (close now; the settling folder stays unsynced until the app next runs).

#### Scenario: Wait and sync

- **WHEN** the user closes the app with files still settling and chooses Wait and sync
- **THEN** the app syncs the folder whole after the files settle and then closes

#### Scenario: Close anyway

- **WHEN** the user chooses Close anyway
- **THEN** the app closes and the folder is uploaded whole by the next run

### Requirement: An incomplete change folder in the storage is held back

A change folder in the storage that this computer has never synced SHALL be held back, not downloaded and not counted, while it is incomplete: it holds notes but no `.openspec.yaml`, or it is a merged item whose source folders are still live in the storage. The duplicate-note-id rule SHALL NOT renumber a source item's notes while a merged item names it. The computer that made the move completes the folder on its next sync, after which it flows normally.

#### Scenario: A half-uploaded merged folder

- **WHEN** the storage holds eight notes of a merged folder and no `.openspec.yaml` and the other computer syncs
- **THEN** nothing of that folder is written to its board, its source items keep their notes and ids, and after the merging computer's next sync the whole merged item arrives

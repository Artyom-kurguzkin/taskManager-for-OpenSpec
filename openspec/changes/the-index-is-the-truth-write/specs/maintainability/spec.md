## ADDED Requirements

### Requirement: A write updates the index in place
A write made by the app or by an agent's tool call SHALL update its own item in the app's index from what it wrote, without re-reading other items, and a burst of writes SHALL raise one change after the burst. The file watcher's echo of the app's own write SHALL change nothing.

#### Scenario: A status change
- **WHEN** the user or an agent changes an item's status
- **THEN** the board shows the new status with no pass over the other items' folders

#### Scenario: A burst of writes
- **WHEN** five subtasks are ticked in a row
- **THEN** the board is told once, after the burst

### Requirement: Outside edits are found by stamps, not by re-reading
The app SHALL find files changed outside it by one directory listing compared to the size and modified time it holds, and SHALL re-read only the entries that differ. It SHALL verify after a file-watcher signal, on an agent tool call when the last verification is older than 2 seconds, and every 5 seconds while a window is focused.

#### Scenario: An outside edit
- **WHEN** an item's file is edited in another editor
- **THEN** the board shows the change after the next verification and only that file is read

#### Scenario: Calls in quick succession
- **WHEN** an agent makes two tool calls within 2 seconds of a verification
- **THEN** the second call lists nothing and reads nothing

### Requirement: Notes and events are read per item
A change to one item SHALL re-read only that item's notes and events; the feed SHALL read the notes of the item it shows.

#### Scenario: A note added
- **WHEN** a note is added to one item
- **THEN** only that item's notes folder is listed and read

### Requirement: The index survives a restart
The app SHALL save its index of each repository between runs, portable across operating systems, SHALL paint the board from it at start before reading any file, and SHALL then verify it by stamps. Settings SHALL offer Rebuild index per open project.

#### Scenario: A second start
- **WHEN** the app starts with a saved index
- **THEN** the board shows the items before any file is read, and only files changed while the app was closed are re-read

#### Scenario: Rebuild index
- **WHEN** the user presses Rebuild index for a project
- **THEN** that project is read again in full and a fresh index is written

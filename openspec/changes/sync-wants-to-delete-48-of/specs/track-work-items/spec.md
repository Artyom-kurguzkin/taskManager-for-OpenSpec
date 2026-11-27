## ADDED Requirements

### Requirement: Sync puts back what nothing explains

The sync SHALL NOT read a missing file or folder as a deletion. A file the last sync had and one side lacks SHALL be removed on the other side only when a move or a line of the item's event log newer than the last sync explains it; otherwise it SHALL be put back. A deletion marker in the storage that nothing explains SHALL be ignored. A sync SHALL never stop to ask about deletions.

#### Scenario: A file gone with nothing to explain it

- **WHEN** a board file is missing on one side and no event log line or move explains it
- **THEN** the sync puts it back on that side and deletes nothing

#### Scenario: A marker nothing explains

- **WHEN** the storage marks a file deleted and nothing explains it
- **THEN** the file is kept on this computer and uploaded again

#### Scenario: Many files missing

- **WHEN** most of the board's files are missing on one side
- **THEN** the sync puts them back and is not paused

### Requirement: Sync follows a change folder by its id

Before it compares files, the sync SHALL look up every change folder that the last sync had and one side lacks: by its id in the archive, through `mergedFrom` (with `mergedFolders`, or the `merged-from` lines of older merges) in a merged item, and by its id under another folder name. It SHALL follow the folder there and merge in the edits the other side made meanwhile.

#### Scenario: Items merged on another computer

- **WHEN** another computer merged nine items into one and this computer syncs without having edited them
- **THEN** the merged item arrives, the nine folders go to the trash and the sync is not paused

#### Scenario: Archived elsewhere while edited here

- **WHEN** one computer archives an item while the other edits it
- **THEN** after both sync, the edit is in the archived copy on both and no active copy is left

### Requirement: Edits to an item merged elsewhere are carried into the merged item

Edits made to an item that another computer merged meanwhile SHALL be carried into the merged item: new event lines, notes, subtasks and list fields are added; a changed title, status, priority, label, date, proposal or delta spec SHALL be written as a note in the merged item instead.

#### Scenario: Merged elsewhere while a note was added here

- **WHEN** one computer merges an item while the other adds a note to it
- **THEN** the note is in the merged item on both computers

#### Scenario: A title changed after the merge

- **WHEN** one computer merges an item while the other changes its title
- **THEN** the merged item keeps its own title and has a note with the changed one

### Requirement: A folder moved on both computers

When both computers moved one change folder to different places, two archives SHALL be joined into the earlier one and two renames into the name that sorts first; any other pair of destinations SHALL both be kept, each with a note naming the other.

#### Scenario: Merged into two items

- **WHEN** one item was merged into different items on two computers
- **THEN** both merged items stay and each has a note naming the other

### Requirement: The storage belongs to one project

The storage's list SHALL record the project's identity (the hash of the repository's first commit). A sync that finds another project's identity SHALL change nothing and say: "This storage holds the board of another project. Nothing was changed. Check the bucket and prefix in Settings." A list without an identity SHALL get it on the next sync; a project without git SHALL skip the check.

#### Scenario: Another project's storage

- **WHEN** the storage's list records another project
- **THEN** nothing is written on either side and the state line shows the message

### Requirement: With no saved base the committed version is the base

When the sync has no saved common version of a file, it SHALL use the file's content at the repository's HEAD as the base of a three-way merge, so edits made on top of it are kept.

#### Scenario: First connection with local edits

- **WHEN** a board file was edited after the last commit and the board is connected to a storage for the first time
- **THEN** the local edits are merged with the storage's version instead of being overwritten

### Requirement: The item's log says where things went and what the sync did

The app SHALL write an event when it archives an item, removes a delta spec, moves specs on a feature rename and removes an attachment. The sync SHALL write a `sync` event on each item it put back, followed or carried edits into. New event lines SHALL carry the time zone offset; lines without one SHALL still read and sort.

#### Scenario: The sync puts a folder back

- **WHEN** the sync restores an item folder that was missing
- **THEN** the item's worklog has a `sync` event saying it was put back

#### Scenario: A removed attachment

- **WHEN** an attachment is removed from a note on one computer
- **THEN** the event log says so and the other computer removes it too instead of putting it back

## ADDED Requirements

### Requirement: Board storage is chosen per project

Each project SHALL have a board storage setting: this folder only (the default, nothing is synced) or a connected storage with a provider and its address. The provider and address SHALL be saved in the project's settings in the repository; keys and secrets SHALL be kept on the computer only and never shown back.

#### Scenario: The default

- **WHEN** a project has no board storage setting
- **THEN** nothing is synced and no sync state is created

#### Scenario: Connecting a folder

- **WHEN** the user chooses connected storage with the Folder provider and a path
- **THEN** the provider and path are saved in the repository's settings and the board syncs with that folder

#### Scenario: A secret

- **WHEN** the user saves an S3 secret access key
- **THEN** it is not written to the repository and the settings show only that it is saved

### Requirement: A storage from the repository needs approval on each computer

A storage named in the repository's settings SHALL NOT be used until this computer has approved that exact provider and address: before approval nothing is built, touched or sent. Saved keys SHALL be bound to the address they were entered for and SHALL NOT be used for another address. Going back to this folder only SHALL remove the keys and the approval from the computer.

#### Scenario: A cloned repository names a folder

- **WHEN** a project whose settings name a Folder storage is opened on a computer that has not approved it
- **THEN** nothing is synced, the folder is not created, and the settings ask whether to allow that storage

#### Scenario: The address changes underneath

- **WHEN** a pull changes the endpoint of a project whose keys are saved
- **THEN** the keys are not sent to the new endpoint and the settings ask again

#### Scenario: Allowing it

- **WHEN** the user presses Allow this storage
- **THEN** the board syncs with that storage

### Requirement: A connected board syncs at start, at exit and after a quiet time

For a connected project the app SHALL sync the board when the project is opened, when the app closes, two minutes after the last change to the board (each further change restarts the wait), and on request. It SHALL NOT sync on a periodic timer. Closing the app SHALL NOT be held up by a sync for more than a fixed limit.

#### Scenario: A change reaches the storage

- **WHEN** the board is changed and no further change follows for two minutes
- **THEN** the board syncs once

#### Scenario: Changes in a row

- **WHEN** another change arrives during the wait
- **THEN** the wait starts again and one sync follows it

#### Scenario: Closing the app

- **WHEN** the app closes while a change has not been synced yet
- **THEN** the board syncs before the app exits, and the app exits within the limit even if the storage does not answer

#### Scenario: Idle

- **WHEN** nothing changes and the app stays open
- **THEN** no sync runs

### Requirement: Changes made on two computers are merged and nothing is lost

When a board file changed on two computers, the app SHALL merge it by its kind (fields, lists, subtasks, text, event log). Where both changed the same thing the later change SHALL win, and the dropped side SHALL be recorded on the work item as a sync-conflict event or a note holding the dropped text.

#### Scenario: The same field on both computers

- **WHEN** the status of one item is changed differently on two computers
- **THEN** the later status is kept and the item's worklog shows a sync-conflict event naming the dropped value

#### Scenario: Different fields

- **WHEN** one computer changes an item's title and the other its priority
- **THEN** both changes are kept

### Requirement: Sync never erases a file

A file the sync removes or replaces on a computer SHALL first be kept in a trash folder in the app's data for 30 days. The sync SHALL remove a board file only when a move (archive, merge, rename) or a line of the item's event log explains it; a missing file SHALL NOT be read as a deletion (see #wi233).

#### Scenario: An explained removal arrives

- **WHEN** a note was renumbered on another computer and its event log says so, and this computer syncs
- **THEN** the old note file leaves the board folder and is in the trash

#### Scenario: A file is replaced

- **WHEN** a sync replaces a board file with the storage's version
- **THEN** the previous content is in the trash

### Requirement: Sync stays inside the board and trusts nothing from the storage

A sync SHALL only read, write, move or delete files inside the project's board folder and its own state folder: it SHALL NOT follow a link out of them, and SHALL refuse a storage path that would leave them. Content from the storage SHALL be checked against its hash before it is written. An S3 endpoint SHALL be https unless it is on the local machine, and a redirect SHALL NOT be followed.

#### Scenario: A link inside the board

- **WHEN** the board folder contains a folder link that points outside it
- **THEN** the files behind the link are not uploaded and nothing is written or moved through it

#### Scenario: A plain http endpoint

- **WHEN** the settings name an http endpoint on another host
- **THEN** the storage is refused with a message

#### Scenario: Wrong content

- **WHEN** the storage returns bytes that do not match the manifest's hash
- **THEN** they are not written into the board

### Requirement: Sync works beside git

The board SHALL stay in the repository. Old content that a checkout or pull brings back SHALL NOT be uploaded: the storage's newer version returns. Sync SHALL pause while a git merge, rebase, cherry-pick or revert is in progress or a board file changed outside the sync holds conflict markers.

#### Scenario: An old branch is checked out

- **WHEN** a checkout rewrites a board file to an earlier version of itself
- **THEN** the next sync restores the newer synced version and uploads nothing for that file

#### Scenario: A merge in progress

- **WHEN** git is in the middle of a merge
- **THEN** the sync writes nothing and the state line says it is paused

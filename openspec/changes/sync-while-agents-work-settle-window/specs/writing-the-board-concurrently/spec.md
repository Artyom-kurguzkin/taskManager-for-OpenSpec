## ADDED Requirements

### Requirement: A change folder is written under a cross-process lock
The store SHALL hold a per-change-folder OS lock (a file under the git common dir's rtm/locks, opened with FileShare.None, shared by every worktree of the repository, released when its holder exits) from the check of what it last read through the temp write to the move, for every file write and every folder delete. It SHALL wait up to 5 seconds for a held lock and then refuse the write with a message naming the lock.

#### Scenario: A writer holds the folder's lock
- **WHEN** another process holds the lock of a change folder and the app writes a file in it
- **THEN** the app waits for the lock, and when it is still held after 5 seconds refuses the write with a message naming the lock, leaving the file untouched

#### Scenario: A worktree and the main repository
- **WHEN** an agent in a git worktree and the app write the same change folder
- **THEN** both take the same lock file

### Requirement: Temp files are unique and crash leftovers are removed
The store SHALL write each file through a temp file named `<file>.<8 hex>.tmp` and SHALL remove the temp files a crashed write left beside a file (its old fixed `.tmp` name and the unique ones) on the next write of that file.

#### Scenario: Two writers
- **WHEN** two writers write the same file
- **THEN** they never share a temp file name

#### Scenario: A crash left a temp file
- **WHEN** a temp file of an earlier crashed write of the same file is beside it and the file is written
- **THEN** the leftover is removed and other files' temp files stay

### Requirement: A stale edit is still refused
The store SHALL refuse a write of an ordinary file whose content on disk is not what the writer last read, with the message that the file changed on disk, and SHALL keep what is on disk.

#### Scenario: A second writer of the same yaml
- **WHEN** the board server changed a .openspec.yaml after the app read it and the app writes it
- **THEN** the app's write is refused with the stale message and the server's content stays

### Requirement: Events are appended, never refused
The store SHALL append an events.log line to the content the file has when the lock is held, not to what the writer last read, so no writer's event is lost or refused as stale.

#### Scenario: Several writers append at once
- **WHEN** several writers, each with its own view of events.log, append events to the same change
- **THEN** every event is in the log

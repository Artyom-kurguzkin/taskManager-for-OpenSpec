## ADDED Requirements

### Requirement: A file unchanged in size and time is not read
The sync SHALL keep the size, last-write time and hash of every file it scanned in the project's state folder, and SHALL NOT read a file again whose size and last-write time are the saved ones.

#### Scenario: A second scan of an unchanged folder
- **WHEN** a folder is scanned twice and no file changed between the scans
- **THEN** the second scan reads no file

#### Scenario: A file edited to the same length
- **WHEN** a file is edited to new content of the same length and its last-write time changes
- **THEN** the next scan hashes it and reports the new hash

#### Scenario: A file written in the same second as the cache
- **WHEN** a file was last written less than two seconds before the scan that saved the cache began
- **THEN** the next scan reads it again

#### Scenario: A missing or damaged cache
- **WHEN** the cache is missing or cannot be read
- **THEN** every file is read and the cache is written again

### Requirement: No scan keeps file bytes
A scan SHALL NOT hold a file's bytes for the rest of the pass; they are read again when an upload, merge or base copy needs them and SHALL still match the scanned hash.

#### Scenario: Bytes are needed after the scan
- **WHEN** a scanned file's bytes are asked for
- **THEN** they are read from the file and equal what was hashed

### Requirement: One scan per pass
The marker repair, the git guard, the scan and the status line SHALL share that cache, and the git guard SHALL read only files whose hash differs from the last synced one; its rules stay: markers only in files changed outside the sync, never inside code fences.

#### Scenario: A guard after a sync with no change
- **WHEN** the git guard runs after a sync and no file changed
- **THEN** no file is opened

#### Scenario: A file changed since the last sync
- **WHEN** one board file changed after the last sync
- **THEN** the guard reads that file only

### Requirement: One connection pool for every S3 storage
Every S3 storage built without a handler of its own SHALL send through one shared handler with a connection lifetime of 5 minutes, no redirects and no cookies, and disposing a storage SHALL NOT dispose that handler.

#### Scenario: Two passes
- **WHEN** two storages are built for two passes
- **THEN** both use the same handler object

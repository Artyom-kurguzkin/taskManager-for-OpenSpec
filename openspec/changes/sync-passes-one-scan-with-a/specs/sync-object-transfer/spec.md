## ADDED Requirements

### Requirement: Objects move six at a time
The sync SHALL download the objects a pass needs and upload the objects it creates up to 6 at a time, SHALL put the manifest only after every object of the round is up, and SHALL record each refused probe as stale exactly as when objects were sent one after another.

#### Scenario: A first sync of eight files
- **WHEN** a computer with an empty folder syncs a board of eight files
- **THEN** six object requests are in flight at once and the other two follow as they finish

#### Scenario: Uploads and the manifest
- **WHEN** a computer uploads eight new files
- **THEN** up to six objects are put at once and the manifest is put last, once

### Requirement: A download is streamed to a file
The sync SHALL write every downloaded object into a temporary file in the project's state folder while its hash is computed, SHALL NOT hold the whole object in memory, SHALL refuse an object over the size cap or whose hash differs, and SHALL copy the file into place only after the local file is checked to be unchanged since the scan.

#### Scenario: A pass downloads files
- **WHEN** a pass downloads objects
- **THEN** each one reaches a file in the state folder as it arrives, no whole copy comes back from the storage as bytes, and the temporary files are gone when the pass ends

### Requirement: A base copy with the same hash is not fetched
When the state folder already holds a base copy whose content has the hash of a needed object, the sync SHALL verify that copy and use it instead of asking the storage.

#### Scenario: An archived item
- **WHEN** a file moved to a new path on another computer and the same content is in this computer's base folder
- **THEN** the file is written here with no object request

### Requirement: The manifest is compact and keeps three earlier versions
The sync SHALL write the manifest as compact JSON with the same members, SHALL keep at most 3 earlier versions per path when it writes a new version, and SHALL still read a manifest with up to 20 earlier versions per path or with indentation.

#### Scenario: Twenty-five edits of one file
- **WHEN** a file is edited and synced 25 times
- **THEN** the manifest text has no line breaks or indentation and the path has 3 earlier versions

#### Scenario: An older client's manifest
- **WHEN** the manifest is indented and a path has 20 earlier versions
- **THEN** it is read as before

### Requirement: A missing manifest entry is a clear failure
When this computer synced a path, edited it since, and the storage's manifest has no entry for it, the sync SHALL stop with a message naming the path and change nothing.

#### Scenario: A manifest that lost an entry
- **WHEN** the manifest has no entry for a path this computer edited
- **THEN** the sync fails, the message names the path, and the local edit is untouched

### Requirement: State files are shared on read and replaced by rename
The sync SHALL read state.json, seen.json, scan.json, pending.json, deletions.json and base copies opening them for shared read, write and delete, and SHALL write each through a unique temporary file renamed over the target.

#### Scenario: A reader while another handle is open
- **WHEN** another handle holds a state file open for writing
- **THEN** the load succeeds

#### Scenario: A save
- **WHEN** a state file is saved
- **THEN** no temporary file is left behind

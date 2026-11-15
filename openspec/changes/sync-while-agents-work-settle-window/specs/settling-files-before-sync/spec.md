## ADDED Requirements

### Requirement: A file still being written waits for the next pass
A sync SHALL leave a file written less than the settle window (10 seconds as the host passes it) before the scan for the next pass, like a link: not uploaded, not overwritten, not taken for a deletion, and listed in the report's Settling list.

#### Scenario: An agent just wrote a file
- **WHEN** a pass runs and a new file was written 3 seconds ago
- **THEN** the file is not uploaded and is in the Settling list, and the next pass after the window uploads it

#### Scenario: A file equal to what was synced
- **WHEN** a file was written a moment ago but equals what was last synced or what the storage holds
- **THEN** it is not put off

### Requirement: A deferral ends after five minutes
A file put off for 5 minutes SHALL be taken anyway, and its deferral starts again afterwards.

#### Scenario: A file that keeps changing
- **WHEN** a file is rewritten before every pass for 5 minutes
- **THEN** the pass 5 minutes after the first deferral uploads it

### Requirement: Temp and editor files are never synced
The scan SHALL NOT list, hash or sync files named *.tmp, ~$*, *.swp, .#*, *~ or .rtm-* (case-insensitive), and a path of that kind in the base or the storage SHALL be left alone in both directions, never taken for a deletion.

#### Scenario: Scratch files beside board files
- **WHEN** a folder holds notes.tmp, page.md~, .#page.md and real.md
- **THEN** only real.md is scanned and uploaded, and the other files stay where they are

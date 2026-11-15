## ADDED Requirements

### Requirement: The sync's own writes do not start a second pass
The sync service SHALL NOT start the quiet wait for a change report explained only by the files the last pass wrote here (downloads, merges, deletions, replaced copies), and SHALL start it for any other change, the app's own write or an outside edit. The pages SHALL still refresh.

#### Scenario: A pass downloads files
- **WHEN** a pass wrote files here and the file watcher reports them afterwards, once or more than once
- **THEN** no wait is pending and no second pass runs

#### Scenario: A change follows the sync's writes
- **WHEN** the user edits in the app, or an editor changes a board file, after such a pass
- **THEN** the two-minute wait starts

### Requirement: An idle computer asks the storage for news
For each connected project the sync service SHALL ask the storage, with one conditional request for the manifest and the version saved by the last pass, whether it changed, every 60 seconds while the window has focus and every 5 minutes while it has not, each interval moved by up to 25 percent either way. It SHALL run a full pass only when the storage answers that the manifest changed.

#### Scenario: Nothing changed in the storage
- **WHEN** the idle interval ends and the manifest is as the last pass saw it
- **THEN** exactly one conditional request is made, no pass runs and the state line is unchanged

#### Scenario: The other computer sent a change
- **WHEN** the idle interval ends and the manifest changed
- **THEN** a full pass runs and brings the change in

#### Scenario: The window loses focus
- **WHEN** the window has no focus at a check
- **THEN** the next check is due in about 5 minutes instead of about 1 minute

### Requirement: The idle check is quiet
The idle check SHALL ask nothing while a pass runs or a wait for one is pending, and SHALL show nothing when the storage cannot be reached; it tries again at the next interval.

#### Scenario: The storage is unreachable
- **WHEN** the conditional request fails
- **THEN** no toast and no state line change appear, and the next check is scheduled

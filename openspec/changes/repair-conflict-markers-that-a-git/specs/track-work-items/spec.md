## ADDED Requirements

### Requirement: Conflict markers that a git merge left in a board file are repaired from the merge's sides

When a work item's .openspec.yaml, tasks.md, a note or events.log holds git conflict markers, the app SHALL rebuild the file before a sync would pause for them. The two sides SHALL come from git, the two parents of the merge commit that holds the markers or HEAD and MERGE_HEAD while the merge is open, with their merge base as the common ancestor, and the sync's own field-wise merge SHALL run over them.

#### Scenario: Markers committed in a merge

- **WHEN** a merge commit holds conflict markers in a work item's .openspec.yaml and the board syncs
- **THEN** the file has no marker lines and the sync is not paused

#### Scenario: Changes on one side only

- **WHEN** only one parent changed a field, or both parents added different entries to a list
- **THEN** that change is kept and the lists are combined

#### Scenario: Subtasks

- **WHEN** the markers are in tasks.md
- **THEN** the subtasks are merged one by one and no marker line is left

### Requirement: The newer side wins a field both sides changed

A field both sides of the merge changed SHALL take the value of the side whose commit has the later committer date. When git cannot name the sides, the hunks inside the file SHALL be the sides, the text outside the markers the common ancestor, and the first side SHALL win what both changed.

#### Scenario: Both parents changed the status

- **WHEN** HEAD's commit is later and its parent set status Blocked and priority high while the other parent set Planned and low
- **THEN** the repaired file has status Blocked and priority high

#### Scenario: Git cannot name the sides

- **WHEN** the markers were edited after the merge commit or were never committed
- **THEN** the file is rebuilt from the hunks in it and the first side wins what both changed

### Requirement: A repair touches the working tree only and says what was lost

The repaired file SHALL be written to the working tree only, never staged or committed, and SHALL count as an edit made in the app so the next sync uploads it. Each place where one side lost SHALL be a sync-conflict event on the work item naming the field, what was kept and what was dropped.

#### Scenario: Nothing is committed

- **WHEN** a file is repaired
- **THEN** HEAD is unchanged, nothing is staged and the sync uploads the file

#### Scenario: The worklog

- **WHEN** the repair kept one side of a field both sides changed
- **THEN** the work item's worklog has a sync-conflict event with the field, the kept value and the dropped value

### Requirement: What cannot be repaired still pauses the sync

A file with markers that has no field-wise rule (proposal.md, a spec), or whose merged result still has markers or is not valid yaml, SHALL be left as it is and the sync SHALL pause for it. A merge that is still open SHALL have its files repaired, and the sync SHALL stay paused until git finishes it.

#### Scenario: A proposal with markers

- **WHEN** proposal.md holds conflict markers
- **THEN** the file is not touched and the sync is paused for it

#### Scenario: A merge in progress

- **WHEN** a git merge is in progress and a board file holds its markers
- **THEN** the file is repaired and the sync stays paused until the merge is committed

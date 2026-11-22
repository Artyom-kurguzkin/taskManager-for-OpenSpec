## ADDED Requirements

### Requirement: A merged-away work item's folder name is never reused
A merge SHALL record the folder names of the items it removes in the merged item (x-rtm.mergedFolders, carried through later merges), and a new work item SHALL NOT take a folder name that is recorded there, even though no folder has that name any more.

#### Scenario: A new item after a merge
- **WHEN** two items named "New work item" are merged and a third item is then created with the same title
- **THEN** the new item's folder is a name neither merged item had

### Requirement: A merged item says where each part of it came from
A merge SHALL mark every event it carries over from a source that is not the main item with that source's number (a trailing " | from #wiN" on the events.log line), keep a mark a line already has when the merged item is merged again, and leave the main item's own events unmarked. The worklog SHALL show "from #wiN" on every such event and on every note a merge brought in (bug, decision or note, whichever filter is on).

#### Scenario: The feed of a merged item
- **WHEN** two items, each with an event and a bug note, are merged
- **THEN** the worklog shows "from #wiN" on the other item's events and on its bug note, also with the Notes filter off, and not on the main item's own entries

#### Scenario: A second merge keeps the first source
- **WHEN** a merged item is merged into a third item
- **THEN** the events that already said "from #wiN" keep that source

## MODIFIED Requirements

### Requirement: Move subtasks to a new work item
Selecting one or more subtasks by clicking their text SHALL offer to move them into a new work item ("Make it a work item" for one, "Move into a separate work item" for several). This SHALL create a new change holding those tasks (renumbered from 1.1), with the same features and contributors, remove them from the source, and renumber the tasks left in the source.

#### Scenario: Split out
- **WHEN** the user selects subtasks 1.4 and 1.5 of #wi123 and moves them
- **THEN** a new work item with those two subtasks exists, #wi123 keeps 1.1–1.3, and both change logs mention the split

#### Scenario: One subtask
- **WHEN** the user selects only subtask 1.3 of #wi123 and picks "Make it a work item"
- **THEN** a new work item holds that subtask as 1.1 and #wi123 keeps the rest

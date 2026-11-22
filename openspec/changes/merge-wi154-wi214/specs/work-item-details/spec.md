## ADDED Requirements

### Requirement: Unresolved bugs count as subtasks
A work item's unresolved bug notes SHALL be listed among its subtasks with a bug mark and counted as open subtasks wherever the subtask count is shown (the item page, board cards, the list and the examine panel); marking a bug resolved SHALL remove it from the list and the count. Bugs are not written to tasks.md.

#### Scenario: An open bug
- **WHEN** an item with no subtasks has one unresolved bug note
- **THEN** Linked tasks reads 0 of 1 done with a BUG row, and the board card reads 0/1 subtasks

#### Scenario: Resolving the bug
- **WHEN** the user ticks the bug's row
- **THEN** the note is marked resolved and the row and its count disappear

### Requirement: The feed navigation reaches bugs and the description
The side menu's worklog days list SHALL start with a Bugs entry counting the unresolved bugs, shown only when there are any, which filters the feed to bugs, and SHALL end with a Description entry that scrolls the feed to the item's description.

#### Scenario: Jumping to the description
- **WHEN** the user clicks Description in the days list
- **THEN** the feed scrolls to the description

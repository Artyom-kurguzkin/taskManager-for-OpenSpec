## ADDED Requirements

### Requirement: Unresolved bugs count as subtasks
A work item's unresolved bug notes SHALL be listed among its subtasks with a bug mark and counted as open subtasks wherever the subtask count is shown (the item page, board cards, the list and the examine panel); marking a bug resolved SHALL remove it from the list and the count. Bugs are not written to tasks.md. Each bug row SHALL name its note as a link (#nN) that opens the worklog at that note, as a subtask made from a note does.

#### Scenario: An open bug
- **WHEN** an item with no subtasks has one unresolved bug note
- **THEN** Linked tasks reads 0 of 1 done with a BUG row, and the board card reads 0/1 subtasks

#### Scenario: Resolving the bug
- **WHEN** the user ticks the bug's row
- **THEN** the note is marked resolved and the row and its count disappear

#### Scenario: From a bug to its note
- **WHEN** the user converts a note to a bug and clicks the bug row's #nN link in Linked tasks
- **THEN** the worklog opens at that note and marks it

### Requirement: The feed navigation reaches the description
The side menu's worklog days list SHALL end with a Description entry that scrolls the feed to the item's description; bugs are reached through the Bugs kind filter, not a row of the days list.

#### Scenario: Jumping to the description
- **WHEN** the user clicks Description in the days list
- **THEN** the feed scrolls to the description

## ADDED Requirements

### Requirement: Subtasks from the worklog
A day's Shift-click menu in the worklog SHALL offer "Add subtask", which adds subtasks from an input in the feed.

#### Scenario: Add from the menu
- **WHEN** the user Shift-clicks today's date, picks Add subtask and types "write tests" and Enter
- **THEN** the item has a subtask "write tests" and the feed shows it as added

### Requirement: Note checkboxes become subtasks
When the user finishes editing a note, every unticked checkbox line in it that does not end with "(created subtask)" SHALL become a subtask of its work item, shown in the feed as added, and the line SHALL stay in the note with " (created subtask)" appended. Ticked lines and lines in code blocks SHALL be left alone.

#### Scenario: A note with checkboxes
- **WHEN** the user writes "- [ ] restrict permissions" in a note and clicks outside the editor
- **THEN** the item has a new subtask "restrict permissions", the feed shows it as added, and the note reads "- [ ] restrict permissions (created subtask)"

#### Scenario: Editing the note again
- **WHEN** the user edits that note again without changing the line
- **THEN** no second subtask is created

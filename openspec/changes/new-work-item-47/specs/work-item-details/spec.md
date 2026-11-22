## ADDED Requirements

### Requirement: The collapsed menu adds worklog entries from an icon
While a work item's worklog is open and the side menu is collapsed, the rail SHALL show an Add entry icon that opens the same list of entries as the expanded menu's Add entry button.

#### Scenario: Adding a note from the collapsed rail
- **WHEN** the side menu is collapsed on a work item and the user clicks the Add entry icon
- **THEN** the menu lists Note, Decision, Bug, Subtask, Link document, Link test and Link commit, and picking Note opens the note editor

## MODIFIED Requirements

### Requirement: Menu in the work item context
While a work item fills the active pane the menu SHALL hide the projects, layout and new-item rows, and show them again on the board. While the window is split the menu SHALL name the pane it acts on ("Tab 1", "Tab 2"; the number alone when collapsed); a window has one side menu, whatever its number of panes. The Worklog block SHALL have one "+ Add entry" button whose menu offers Note, Decision, Bug, Subtask, Link document, Link test and Link commit.

#### Scenario: Add a bug
- **WHEN** the user clicks + Add entry and picks Bug
- **THEN** a bug entry opens in the feed's editor

#### Scenario: Tab named
- **WHEN** the window is split in two and the right pane is active
- **THEN** the side menu reads "Tab 2"

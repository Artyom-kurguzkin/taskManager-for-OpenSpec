## ADDED Requirements

### Requirement: Side menu without checkboxes
Every on/off option in the side menu SHALL be a pill toggle: filled when on, underlined text when off. The top row SHALL be a collapse icon only. Under Organise, "sort by" SHALL come first. "show all hidden" SHALL sit under the Hidden header.

#### Scenario: Toggle a filter
- **WHEN** the user clicks the Events pill in the Worklog block
- **THEN** it turns from filled to underlined and events leave the feed

### Requirement: Menu in the work item context
While a work item fills the window the menu SHALL hide the projects, layout and new-item rows, and show them again on the board and in split view. The Worklog block SHALL have one "+ Add entry" button whose menu offers Note, Decision, Bug, Subtask, Link document, Link test and Link commit.

#### Scenario: Add a bug
- **WHEN** the user clicks + Add entry and picks Bug
- **THEN** a bug entry opens in the feed's editor

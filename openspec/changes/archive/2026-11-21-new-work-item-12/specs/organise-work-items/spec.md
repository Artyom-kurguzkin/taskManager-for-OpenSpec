## ADDED Requirements

### Requirement: Adding a project keeps the board arrangement
The board SHALL keep showing the arrangement (column order, hidden columns, grouping, filters) of the project the user was viewing when another project is added to the workspace. Only the first project opened and the user's choice of a project tab SHALL change which project's saved view the board shows.

#### Scenario: A project is added while another is shown with a custom column order
- **WHEN** the user has moved the Research column to the far left of project A's board and then adds project B
- **THEN** the board still shows project A's view with Research first, and B's statuses that A lacks appear where B's own status list puts them

#### Scenario: The user chooses a project's tab
- **WHEN** the user clicks project B's tab
- **THEN** the board shows B's saved view

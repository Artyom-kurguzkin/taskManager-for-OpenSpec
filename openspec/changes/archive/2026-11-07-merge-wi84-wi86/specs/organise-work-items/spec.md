## ADDED Requirements

### Requirement: Several projects on one board
With several repositories open as tabs, the board SHALL stack a block per project, named after its folder, with a chevron that collapses it and a menu that hides it. A lone visible project SHALL show no project header. When every project has the same columns, one header row above SHALL serve them all; otherwise each block keeps its own column headers.

#### Scenario: Two projects, status grouping
- **WHEN** two repositories are open and the board is grouped by status
- **THEN** one row of the seven status headers sits above two named blocks of cards

#### Scenario: Collapse and hide
- **WHEN** the user clicks a project's chevron, then chooses Hide project from its menu
- **THEN** the block folds to its heading, then disappears; the side menu lists it under Hidden and its eye brings it back

### Requirement: Projects in the list
With several repositories open, the list SHALL group rows by project first, then by column.

#### Scenario: Two projects in the list
- **WHEN** two repositories are open and the list is shown
- **THEN** a group row per project heads its column groups, and a work item number present in both appears once under each

### Requirement: Cards stay in their project
A card SHALL move only within its own project: a drop into another project's column SHALL leave it where it was and say so. Selection and the arrow keys SHALL act within the project of the selected cards. Opening a card of another project SHALL make that project the active repository, then open the card.

#### Scenario: Cross-project drop
- **WHEN** a card of the first project is dropped into a column of the second
- **THEN** nothing changes in either repository and a message says the work item stays in its repository

#### Scenario: Open from another project
- **WHEN** the user clicks a card in the second project's block
- **THEN** the second repository becomes the active one and the card's page opens

### Requirement: Reorder projects
A project SHALL be selected by Shift-clicking (or Ctrl/⌘-clicking) anywhere in its heading row on the board, or its group row in the list (except its buttons), shown as a tinted row and never selecting text, and the up and down arrows SHALL move the selected projects past the next unselected one, as selected cards move. The order SHALL be the tabs' order and SHALL be remembered.

#### Scenario: Move a project up
- **WHEN** the user Shift-clicks the second project's heading and presses the up arrow
- **THEN** it becomes the first project on the board and in the list, and stays first after a restart

### Requirement: Remove a project
The repository menu SHALL end with "Remove a project…" while more than one project is open. It SHALL ask which project to remove and close that project's tab; nothing on disk SHALL change, and the last project SHALL not be removable.

#### Scenario: Remove one of two
- **WHEN** the user chooses Remove a project… and picks demo2
- **THEN** demo2 leaves the board and the tabs, its folder stays, and the menu no longer offers Remove a project…

### Requirement: New work item picks its project
With several projects open, creating a work item from the side menu or from the shared header row SHALL first ask which project it goes to; the item SHALL be created there and that project SHALL become active. With one project open it SHALL be created at once, without asking. A column's own "+ Add work item here" SHALL use that column's project.

#### Scenario: Two projects
- **WHEN** two projects are open and the user presses New work item and picks demo2
- **THEN** the item is created in demo2, demo2 becomes the active project, and the item's page opens

### Requirement: Sticky column headers
The board's column headers SHALL stay at the top while the board scrolls: the shared header row with several projects, or each column's own header otherwise.

#### Scenario: Scroll down
- **WHEN** the user scrolls the board down
- **THEN** the column names stay visible at the top

### Requirement: Project selection lets go
Selecting a project SHALL let go of selected cards, and selecting a card SHALL let go of selected projects; a plain click on the board or list SHALL let go of selected projects.

#### Scenario: Move on to a card
- **WHEN** a project is selected and the user Shift-clicks a card
- **THEN** only the card is selected

### Requirement: One selection look
Anything selected with Shift-click — a card, a list row, a project heading, a subtask — SHALL look the same: it keeps its own surface, a line in the text colour hugs it and a dotted yellow line runs round that. Selection SHALL never be shown as a fill.

#### Scenario: Card and row
- **WHEN** the user Shift-clicks a card on the board and a row in the list
- **THEN** both show the same dotted yellow line round a solid line, and neither changes its fill

## MODIFIED Requirements

### Requirement: Complete by archiving
Dropping a card on Completed SHALL ask for confirmation and then archive the change with the OpenSpec CLI. Before the CLI runs, the app SHALL refresh the change's delta specs: every MODIFIED requirement SHALL get, appended verbatim from the current main spec, the scenarios it lacks, so a requirement that another archive changed since the delta was written never makes the archive fail. Cards SHALL NOT be dragged out of Completed.

#### Scenario: Archive
- **WHEN** the user drops #wi123 on Completed and confirms
- **THEN** the change folder moves to `openspec/changes/archive/<date>-<name>/`, its delta specs merge into `openspec/specs/`, and the card shows under Completed

#### Scenario: Stale modified requirement
- **WHEN** #wi107 modifies "Send money" with one scenario and the main spec has since gained "Fee shown" and "Limit"
- **THEN** both are appended to #wi107's block before the archive, the archive succeeds and the main spec keeps all three with #wi107's text

#### Scenario: Archive fails
- **WHEN** the CLI reports an error, for example unfinished tasks or invalid deltas
- **THEN** the card returns to its column and the CLI's message is shown

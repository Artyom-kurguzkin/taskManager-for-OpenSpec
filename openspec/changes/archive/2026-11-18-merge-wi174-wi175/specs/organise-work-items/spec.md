## ADDED Requirements

### Requirement: The menu has a commit selector
The menu's Filters section SHALL have a commit selector whose choices are any (the default), uncommitted, and the newest commits of the repository (short hash, subject, day). The choice SHALL be saved with the board's other options, and the folded Filters header SHALL name it.

#### Scenario: Choosing a commit
- **WHEN** a commit is chosen in the selector
- **THEN** the choice is saved with the board and the Filters header says commit and its short hash

### Requirement: The board shows only the work items of the chosen commit
WHEN a commit is chosen, the board, the list and the calendar SHALL show only the work items whose change folder holds a file that commit changed, an archived change counting under its archived folder. WHEN uncommitted is chosen they SHALL show only the work items whose files are changed or new in the work tree. Any SHALL leave no work item out.

#### Scenario: A commit is chosen
- **WHEN** the selector is set to a commit
- **THEN** only the work items that commit changed are shown, together with the other filters

#### Scenario: Uncommitted is chosen
- **WHEN** the selector is set to uncommitted
- **THEN** only the work items with changed or new files are shown

#### Scenario: Any
- **WHEN** the selector is set to any
- **THEN** no work item is left out for it

### Requirement: Work items with uncommitted changes are marked
WHILE the commit selector is on any, a card of a work item whose files are changed or new in the work tree SHALL carry one small muted dot before its title.

#### Scenario: One item is edited
- **WHEN** a work item's files have uncommitted changes and no commit is chosen
- **THEN** its card shows the dot and the cards of committed items do not

### Requirement: A work item shows its last contributing commit
The work item page header and the examine panel SHALL show the newest commit that changed the item's folder (short hash, with the subject), or that the item has uncommitted changes while its files are changed or new; nothing when git knows nothing of it.

#### Scenario: Opening an item
- **WHEN** a work item's folder was last changed by a commit
- **THEN** its page header shows that commit's short hash and the examine panel shows the hash and subject

#### Scenario: An item is edited since
- **WHEN** a work item has files that are not committed yet
- **THEN** both show that it has uncommitted changes

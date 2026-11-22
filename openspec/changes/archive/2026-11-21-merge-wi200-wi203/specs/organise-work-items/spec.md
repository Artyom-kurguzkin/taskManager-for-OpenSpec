## ADDED Requirements

### Requirement: A project's section shows only its own statuses
With several projects open and the board grouped by status, the system SHALL show in a project's section only the statuses that project's own openspec/config.yaml lists. The shared header row SHALL list the union of the projects' statuses in the saved order, shown once; under that header a project without a status SHALL show an empty, visibly unavailable cell instead of a working column, and the list SHALL show no group for a status the project lacks.

#### Scenario: A status only one project has
- **WHEN** two projects are open and only one of them has a Research status
- **THEN** the header row shows Research once
- **AND** the other project's Research cell is empty and marked unavailable, and its list has no Research group

### Requirement: Moving a card to a status its project lacks asks to add it
When a card is dropped, or moved with the arrow keys, onto a status its project does not have, the system SHALL ask "Add <status> to <project>?" and SHALL NOT fail with a message. On yes the status is added to that project's openspec/config.yaml before Completed and the card moves; on no nothing changes.

#### Scenario: Yes adds the status and moves the card
- **WHEN** the user drops a card of a project without Research onto the Research cell and answers yes
- **THEN** that project's config.yaml lists Research and the card has the status Research

#### Scenario: No changes nothing
- **WHEN** the user answers no
- **THEN** the project's config.yaml and the card are unchanged

## ADDED Requirements

### Requirement: Deleting a status
A status that is not one of the project's default statuses (Backlog, In Progress, Completed) SHALL be deletable from Settings. Open work items that have it SHALL move to the project's first status, after a confirmation that names how many move; archived work items SHALL keep the deleted name in their file and are not rewritten. Open work items SHALL move whatever they wait on: an item waiting on an open dependency does not keep the status in place.

#### Scenario: Open items move to the first status
- **WHEN** the user confirms removing a status that two open work items have
- **THEN** the status leaves the project's list and both items have the project's first status

#### Scenario: An item waiting on a dependency moves too
- **WHEN** the user confirms removing a status that an open work item has while that item waits on an unresolved dependency
- **THEN** the status leaves the project's list and no item of the status is left behind or half moved

#### Scenario: Archived items keep the name
- **WHEN** a status is removed that an archived work item was in when it was archived
- **THEN** the archived item's file still names that status and the item still shows as Completed

#### Scenario: A default status stays
- **WHEN** the user tries to remove Backlog, In Progress or Completed
- **THEN** the status is kept and Settings offers no remove button for it

### Requirement: Deleting a status or a feature from the viewers
The board's column header menu and the list's group header menu SHALL offer "Delete status…" on a custom status and "Delete feature…" on a feature, with the confirmation Settings uses. A default status, a merged column, Unassigned and any other grouping SHALL offer no delete.

#### Scenario: Delete a status from the board column
- **WHEN** the user opens the menu of a custom status column and chooses Delete status…, then confirms
- **THEN** the column's open work items move to the project's first status and the status leaves that project's openspec/config.yaml

#### Scenario: A default status has no delete
- **WHEN** the user opens the menu of the Backlog column or the Backlog group
- **THEN** there is no Delete status… entry

#### Scenario: Delete from the list's group header
- **WHEN** the user opens the menu on a custom status group of the list and confirms Delete status…
- **THEN** the group leaves the list and its items are in the first status's group

#### Scenario: Delete a feature from the list's group header
- **WHEN** the list is grouped by feature and the user chooses Delete feature… on a feature group and confirms
- **THEN** the group leaves the list and its open work items are in Unassigned

#### Scenario: Several projects
- **WHEN** two projects are open and both have the status the user deletes from the shared header row
- **THEN** the user is asked which project, and only that project loses the status

### Requirement: Deleting a feature
Deleting a feature SHALL remove it from its open work items, which become unassigned, and SHALL leave archived work items on it.

#### Scenario: Archived items stay on a deleted feature
- **WHEN** a feature is deleted that an open and an archived work item have
- **THEN** the open item has no feature and the archived item still lists the deleted feature

### Requirement: Merging items whose feature was deleted
Merging work items SHALL accept items that share the same deleted feature, SHALL give the merged item the existing feature when another source has one that still exists, and SHALL refuse to merge items that have two different features that no longer exist, saying which.

#### Scenario: The same deleted feature merges
- **WHEN** two archived items with the same deleted feature are merged
- **THEN** the merged item keeps that feature

#### Scenario: An existing feature takes over
- **WHEN** an item with a deleted feature is merged with an item whose feature exists
- **THEN** the merged item has the existing feature

#### Scenario: Two different deleted features are refused
- **WHEN** two items with different deleted features are merged
- **THEN** the merge is refused with a message naming both features and neither item is changed

### Requirement: The user's column order wins
The system SHALL show every board column in the board's saved order, with one project open or several, under a shared header row or in each project's section, and SHALL save every move of a column (drag, Left and Right on a Shift-clicked header) with the board, so it survives a restart. A status the saved order does not hold yet SHALL take the place the projects' status lists give it.

#### Scenario: Dragging a status of one project in the shared header
- **WHEN** two projects are open, only one has Research, and the user drags the Research header in front of Blocked
- **THEN** the header row shows Research in front of Blocked and the other project's unavailable Research cell is in the same place
- **AND** after the app is closed and opened again the order is the same

#### Scenario: Arrow keys on a selected column
- **WHEN** the user Shift-clicks the Research header and presses Left
- **THEN** Research moves one place left, one more for each further press, with one project open as with several

#### Scenario: A status the saved order lacks
- **WHEN** the saved order was written before a project's status existed
- **THEN** that status shows where the project's list puts it, not after every column the order holds

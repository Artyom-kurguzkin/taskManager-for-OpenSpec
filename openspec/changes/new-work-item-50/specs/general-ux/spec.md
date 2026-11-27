## ADDED Requirements

### Requirement: A group offers the same actions in the board, the list and the calendar
A board column header, a list group row and a calendar group SHALL open one menu of the same actions, built from one definition. A list group row SHALL show no separate options button: a click on the group's name SHALL open the menu, and the chevron SHALL still fold the group.

#### Scenario: Hiding a status from the list
- **WHEN** the user clicks a status group's name in the list and chooses Hide group
- **THEN** the group leaves the list and its column key is in the board's hidden list, as when the column is hidden on the board

#### Scenario: The list group menu matches the board column menu
- **WHEN** the user opens the menu of a status group in the list and of the same status column on the board
- **THEN** both list the same actions in the same order: add a work item, select all, hide, rename and delete status, differing only in the word group for column

#### Scenario: A core status has no rename or delete
- **WHEN** the user opens the menu of the Backlog, In Progress or Completed group
- **THEN** the menu offers no Rename status and no Delete status

#### Scenario: Folding a group
- **WHEN** the user clicks the chevron of a list group row
- **THEN** the group folds and its menu stays closed

#### Scenario: A calendar group
- **WHEN** the user clicks a group's name in the calendar
- **THEN** the same menu opens, without Select all because the calendar shows no selection

#### Scenario: A feature group
- **WHEN** the user opens the menu of a feature column or group
- **THEN** it offers Rename feature and Delete feature beside the common actions

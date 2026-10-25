## ADDED Requirements

### Requirement: Card contents
An expanded card SHALL show its title band, its features as chips (its status instead when grouped by bucket, since the column is the feature), its date, contributors and tags. It SHALL NOT show a done/total subtask count.

#### Scenario: Features on cards
- **WHEN** the board is sorted by feature and a card is linked to Board and Work item editor
- **THEN** its expanded form shows a "Board" chip and a "Work item editor" chip

#### Scenario: No subtask count
- **WHEN** a card's work item has 5 of 7 subtasks done
- **THEN** the expanded card does not show "5/7"

### Requirement: Hidden column counts
Each hidden column listed in the expanded side menu SHALL show how many items it holds (subtasks when the board shows subtasks), under the current contributor filter and "show hidden" setting, so the user can tell whether a hidden column is worth bringing back.

#### Scenario: Count of a hidden column
- **WHEN** Blocked, which holds one work item, is hidden
- **THEN** the side menu lists Blocked with the number 1 before its show control

### Requirement: Features in a list row
In the list view, a work item's features SHALL stay on one line in the Features cell: the first as a dashed chip (as on a card) and the rest as a "+N" count, with every feature named in the cell's tooltip, so a row keeps its height and the column its width.

#### Scenario: Three features
- **WHEN** a work item is linked to App shell, Board and Work item editor
- **THEN** its Features cell shows "App shell" as a dashed chip and "+2", and its tooltip names all three

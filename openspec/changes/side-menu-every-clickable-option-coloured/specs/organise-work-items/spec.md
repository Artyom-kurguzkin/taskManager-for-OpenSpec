## ADDED Requirements

### Requirement: Show all hidden brings back what was put aside
With "show all hidden" on, hidden columns, rows, grid columns, hidden projects and deleted work items SHALL come back on the board, the list and the calendar. Only the contributor filter, the due period, the changed period and a project's collapse SHALL still leave work items out.

#### Scenario: A hidden project comes back and counts
- **WHEN** a project is hidden and not collapsed and the user switches "show all hidden" on
- **THEN** the project is drawn with its cards and its work items count in the column headers

### Requirement: A collapsed project stays collapsed and counts nowhere
A project collapsed to its heading SHALL stay collapsed, on the board and in the list, whatever "show all hidden" says. Its work items SHALL be counted nowhere: not in the shared column header counts, the list's group counts or the side menu's Hidden counts.

#### Scenario: Show all hidden does not open a collapsed project
- **WHEN** two projects are open, the second is collapsed to its heading, and the user switches "show all hidden" on
- **THEN** the second project stays collapsed on the board and in the list, and the first project's deleted work items come back

#### Scenario: A collapsed project counts nowhere
- **WHEN** every project that has In Progress work items is collapsed and the user switches "show all hidden" on
- **THEN** the In Progress column header counts none of those work items, and the side menu's Hidden counts add up only projects that are not collapsed

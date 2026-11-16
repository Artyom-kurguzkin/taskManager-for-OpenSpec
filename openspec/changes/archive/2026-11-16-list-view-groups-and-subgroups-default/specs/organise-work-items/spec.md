## ADDED Requirements

### Requirement: The list has its own groups and subgroups
The List view SHALL name its two Organise options "groups" and "subgroups", both offering "none" first and every field the board's columns offer, and both SHALL be none until the user chooses, so a fresh list is flat: no group headers, every item once, in the sort order. The board SHALL keep "columns" (status by default) and "rows" (none by default), and a choice made in one view SHALL NOT change the other.

#### Scenario: A fresh list is flat
- **WHEN** the user opens the List view on a repository with no saved list choice
- **THEN** the options read "groups: none" and "subgroups: none", the grid has no group header rows and shows the Status column

#### Scenario: Choosing a group
- **WHEN** the user picks "status" as the list's groups
- **THEN** the rows are grouped by status, the Status column is left out, the choice is saved, and the board's columns are still status

#### Scenario: A saved list keeps its grouping
- **WHEN** a repository's saved settings hold a list view grouped by columns and rows from before the list had its own
- **THEN** the list opens grouped by them

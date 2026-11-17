# board Specification

## Purpose
TBD - created by archiving change arrow-keys-list-row-placement. Update Purpose after archive.

## Requirements

### Requirement: Arrow keys step in the shown order
When rows or cards are selected and the user presses ArrowUp or ArrowDown, the board and the list SHALL move them one step in the order they are shown in, including an order made by a column sort of the list's grid, and SHALL change the place of no other row. Arranging by hand SHALL drop the grid's column sort so the new order is the one shown.

#### Scenario: A row in a sorted list steps over the row shown above it
- **WHEN** the list is sorted by a column header and the user selects a row in the middle of a group and presses ArrowUp
- **THEN** the row swaps with the row shown directly above it
- **AND** every other row keeps its place

#### Scenario: A row across priority groups of a flat list
- **WHEN** the list shows high-priority rows before low-priority ones and the user presses ArrowUp on the first low-priority row
- **THEN** it swaps with the last high-priority row shown above it and nothing else moves

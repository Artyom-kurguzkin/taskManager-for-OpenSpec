## ADDED Requirements

### Requirement: An arrow key moves exactly one row in the shown order
In the list, an arrow key (up or down) on a selected row SHALL work on the order the user sees, whatever header sort or saved manual order is set. A press that would move nothing SHALL change nothing. A press that moves a row SHALL make the shown order of every group the manual order, drop the header sort and move that row one place, so the moved row is the only visible change.

#### Scenario: A press with nowhere to go
- **WHEN** the grid is sorted by a column and the first shown row is pressed with the up arrow, or the last shown row with the down arrow
- **THEN** the shown order and the header sort stay exactly as they were and nothing is saved

#### Scenario: A row at the edge of the board's order but not of the shown order
- **WHEN** the grid is sorted by a column and a row that is first in the board's order but shown last in its group is pressed with the up arrow
- **THEN** it swaps with the row shown above it and no other row changes place

#### Scenario: A row crossing into the neighbouring group
- **WHEN** the first row of a group is pressed with the up arrow and a saved order names cards of other groups
- **THEN** only that row changes place; every other group keeps its shown order

### Requirement: The header sort stays usable after the arrow keys
A click on a sortable column header SHALL sort the list again after an arrow key took the header sort away.

#### Scenario: Sorting again
- **WHEN** an arrow key moved a row and dropped the header sort, and the Updated header is then clicked
- **THEN** the list is sorted by Updated again

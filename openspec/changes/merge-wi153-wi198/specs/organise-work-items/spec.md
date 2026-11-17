## MODIFIED Requirements

### Requirement: Rows drag from anywhere
In the list, Shift-click SHALL mark a row as selected, and only a selected row SHALL drag. A list row has no drag handle: pressing a selected row anywhere (the title, the cell's padding, a pill, empty cell space, the row's edge) and moving a few pixels SHALL drag it at once, without holding, as cards drag; every selected row SHALL move with it, to the same place and in their current order. A press on a row that is not selected SHALL never drag: moving selects cells, as in a spreadsheet. Releasing a press without moving SHALL act as a click: a plain click opens the item, Shift-click toggles its selection, and a click on a pill opens its dropdown. Dropping with the pointer over a group's own header row SHALL put the item at the end of that group, not of the group above. Titles SHALL line up under the Title header whether or not their row has subtasks. With one or more rows selected, the Up and Down arrow keys SHALL move them as described under Move rows with the keyboard.

#### Scenario: Shift-click selects
- **WHEN** the user Shift-clicks a row in the list
- **THEN** the row is marked selected, and Shift-clicking it again unselects it

#### Scenario: Drag a selected row
- **WHEN** the user presses a selected Backlog row, anywhere on it, and moves it onto the Planned group
- **THEN** the item's status becomes Planned

#### Scenario: Drag several selected rows
- **WHEN** the user has two Planned rows selected, presses one of them and drags it onto the Confirmed group
- **THEN** both items' status becomes Confirmed, and they land in the order they had

#### Scenario: Press on an unselected row
- **WHEN** the user presses a row that is not selected, on its text or off it, and moves to another cell
- **THEN** cells are selected and no row drags

#### Scenario: Hold and drag
- **WHEN** the user presses a selected row and holds without moving
- **THEN** nothing lifts, and the drag starts only once the pointer moves

#### Scenario: Click
- **WHEN** the user clicks a row's title without moving, or a pill
- **THEN** the item opens, or the pill's dropdown opens

### Requirement: Move rows with the keyboard
With rows or cards selected (Shift-click), the arrow keys SHALL move them, whatever has the keyboard focus, as long as the user is not typing in a field. They SHALL step inside the cell the selection is in: a group's rows in one subgroup in the list (the whole group without subgroups), a column's cards in one row on the board (the whole column without rows); the neighbours are the items of that cell only. Up and Down SHALL swap the selection with its neighbour one place per press, and the sort order SHALL become my order, as a drag does. In the list, when the selection sits together at the cell's edge in the key's direction, Up and Down SHALL carry it into the neighbouring subgroup that has rows (the end of it going up, the start going down) and set the subgroup field; past the group's last (first) subgroup into the neighbouring group (setting the group field), entering its nearest subgroup and setting that field too, or, when that group has no rows, keeping the selection's own subgroup value; with nothing beyond, nothing moves. On the board Up and Down at the edge of a cell SHALL carry the cards into the neighbouring row's cell in the same column (empty or not), and stop at the first and last row and column end. Left and Right on the board SHALL carry the cards into the neighbouring column's cell in the same row at the same height. The selection and the keyboard focus SHALL stay on what moved, and the moved row or card SHALL be scrolled into view, clear of the list's sticky header, the board's sticky column header and the selection bar.

#### Scenario: Arrow after selecting a row
- **WHEN** the user Shift-clicks a row and presses the up arrow
- **THEN** the row moves up one place and the page does not scroll

#### Scenario: Arrow inside a subgroup
- **WHEN** the list is grouped by status with subgroups by priority and the user Shift-clicks the second row of a Lower priority subgroup and presses Up
- **THEN** it is the first row of that subgroup, with its status and priority unchanged

#### Scenario: Arrow at the top of a subgroup
- **WHEN** the user presses Up again
- **THEN** its priority becomes high and it is the last row of the High priority subgroup of the same group

#### Scenario: Arrow at the top of a group
- **WHEN** the selected row is the first of the group's first subgroup and the user presses Up
- **THEN** its status becomes that of the group above and it is the last row of that group's last subgroup that has rows

#### Scenario: Arrow down comes back
- **WHEN** the row is the last of its group's last subgroup and the user presses Down
- **THEN** it becomes the first row of the first subgroup that has rows in the group below

#### Scenario: No group beyond
- **WHEN** the user selects the first row of the first group and presses Up
- **THEN** nothing changes

#### Scenario: Several rows at the edge
- **WHEN** the user selects the last two rows of a group without subgroups and presses Down
- **THEN** both move to the start of the next group, in the order they had

#### Scenario: Board card with rows
- **WHEN** the board has rows by priority and the user selects the second card of a lower-priority cell and presses Up
- **THEN** it is the first card of that cell and still selected; one more press moves it to the end of the same column's cell in the high-priority row, and another to the top of that cell; at the top of the first row it stays

#### Scenario: Selection follows
- **WHEN** a move takes the selected card or row out of the visible part of the scrolling area
- **THEN** the area scrolls just enough to show it again

### Requirement: Grouping is not repeated
Whatever the board or list is grouped by SHALL NOT be shown again inside a group, except the list's Status column: the list SHALL hide Label when grouped by label, Feature by feature and Contributors by contributor, and SHALL always show Status, whatever the grouping (and with subgroups), since the organising operators work on it, unless the user hides that column themselves from its header. A grid layout saved while the column was dropped SHALL NOT keep it hidden. A card SHALL leave out its label when grouped by label and its contributors when grouped by contributor.

#### Scenario: List grouped by status
- **WHEN** the list is grouped by status
- **THEN** it still has a Status column; grouped by label it has a Status column and no Label column

#### Scenario: Status column after a saved layout
- **WHEN** the list was last used grouped by status and its saved layout has Status hidden, and the user opens it with groups none
- **THEN** the Status column shows

#### Scenario: The user hides it
- **WHEN** the user clicks the × in the Status header
- **THEN** the column is hidden and listed under Hidden in the side menu

#### Scenario: Card grouped by label
- **WHEN** the board is grouped by label
- **THEN** a card shows no label tag; grouped by status it shows it

### Requirement: Colour by any field
Every card band, list row and roadmap bar SHALL take its colour from the colour field: status, label and priority by their own colours, due and start dates by period, feature and contributor by a stable colour per value, none in a neutral colour. In the list the colour SHALL tint the whole row (a light wash of the colour over every cell, the text keeping the theme's colour so it reads in the light and the dark theme), not a strip at its edge; the ring of a selected row and the mark of the open row SHALL stay visible over it, and with colour none the rows are not tinted. A legend under the colour choice SHALL name the colours shown.

#### Scenario: Colour by label
- **WHEN** colour is by label
- **THEN** bug cards are peach, feature cards sky and enhancement cards sage, and the legend lists them

#### Scenario: Whole row
- **WHEN** the list is coloured by status
- **THEN** every cell of a Planned row has a light blue wash, and a Shift-selected row keeps its ring

#### Scenario: Colour none
- **WHEN** the colour choice is none
- **THEN** no list row is tinted

### Requirement: The list has its own groups and subgroups
The List view SHALL name its two Organise options "groups" and "subgroups", both offering "none" first and every field the board's columns offer, and both SHALL be none until the user chooses, so a fresh list is flat: no group headers, every item once, in the sort order. The board SHALL keep "columns" (status by default) and "rows" (none by default), and a choice made in one view SHALL NOT change the other.

#### Scenario: A fresh list is flat
- **WHEN** the user opens the List view on a repository with no saved list choice
- **THEN** the options read "groups: none" and "subgroups: none", the grid has no group header rows and shows the Status column

#### Scenario: Choosing a group
- **WHEN** the user picks "status" as the list's groups
- **THEN** the rows are grouped by status, the Status column stays, the choice is saved, and the board's columns are still status

#### Scenario: A saved list keeps its grouping
- **WHEN** a repository's saved settings hold a list view grouped by columns and rows from before the list had its own
- **THEN** the list opens grouped by them

## ADDED Requirements

### Requirement: Groups move like columns
In the list, Shift-click on a group or subgroup header SHALL select it, marked as a selected row is, and Up and Down SHALL then move it among its siblings one place per press, in the same saved order the board's columns (rows, for subgroups) use. The grid's header sort SHALL order only the rows inside each group: groups and subgroups SHALL keep their saved order.

#### Scenario: Select and move a group
- **WHEN** the list is grouped by status, the user Shift-clicks the Planned group header and presses Up
- **THEN** Planned is above the group that was above it and still selected, and the board's columns show Planned in that place too

#### Scenario: A plain click selects nothing
- **WHEN** the user clicks a group header's name without Shift and presses Down
- **THEN** nothing is selected and no group moves, and the chevron still folds the group

#### Scenario: A subgroup moves among its siblings
- **WHEN** the list is grouped by status with subgroups by priority and the user Shift-clicks a Low subgroup header and presses Up
- **THEN** the Low subgroup is above the High one in every group, the groups keep their order, and no row changes group

#### Scenario: A header sort keeps the group order
- **WHEN** the list is grouped by status with Backlog above Planned, and the user clicks the Updated header
- **THEN** the rows inside each group are ordered by Updated, and the groups stay in their saved order, the group of the oldest row not moving to the top

## MODIFIED Requirements

### Requirement: Rows drag from anywhere
In the list, Shift-click SHALL mark a row as selected, and only a selected row SHALL drag. A list row has no drag handle: pressing a selected row anywhere (the title, the cell's padding, a pill, empty cell space, the row's edge) and moving a few pixels SHALL drag it at once, without holding, as cards drag; every selected row SHALL move with it, to the same place and in their current order. A press on a row that is not selected SHALL never drag: moving selects cells, as in a spreadsheet. Releasing a press without moving SHALL act as a click: a plain click opens the item, Shift-click toggles its selection, and a click on a pill opens its dropdown. Dropping with the pointer over a group's own header row SHALL put the item at the end of that group, not of the group above. Titles SHALL line up under the Title header whether or not their row has subtasks. With one or more rows selected, the Up and Down arrow keys SHALL move them within their group, and when they sit together at the group's edge in the key's direction they SHALL leave it, into the end of the group above (Up) or the start of the group below (Down) by whatever the list is grouped by, exactly as a mouse drop there would place them; with no group beyond, nothing moves.

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

#### Scenario: Arrow inside a group
- **WHEN** the list is grouped by status and the user Shift-clicks the second Planned row and presses Up
- **THEN** it is the first Planned row and keeps its status

#### Scenario: Arrow at the top edge
- **WHEN** the user Shift-clicks the first Planned row and presses Up
- **THEN** its status becomes Backlog and it is the last Backlog row

#### Scenario: Arrow at the bottom edge with several rows
- **WHEN** the user selects the last two Planned rows and presses Down
- **THEN** both become Confirmed, in the order they had, at the start of that group

#### Scenario: No group beyond
- **WHEN** the user selects the first row of the first group and presses Up
- **THEN** nothing changes

### Requirement: Move rows with the keyboard
With rows selected (Shift-click, as on the board; the list has no tick boxes), the arrow keys SHALL move them as on the board, whatever has the keyboard focus, as long as the user is not typing in a field. In the list, Up and Down at the edge of a group SHALL carry the rows into the neighbouring group (see Rows drag from anywhere); on the board a card stops at the end of its column.

#### Scenario: Arrow after selecting a row
- **WHEN** the user Shift-clicks a row and presses the up arrow
- **THEN** the row moves up one place and the page does not scroll

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

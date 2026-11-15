## MODIFIED Requirements

### Requirement: Rows drag from anywhere
In the list, Shift-click SHALL mark a row as selected, and only a selected row SHALL drag. A list row has no drag handle: pressing a selected row anywhere (the title, the cell's padding, a pill, empty cell space, the row's edge) and moving a few pixels SHALL drag it at once, without holding, as cards drag; every selected row SHALL move with it, to the same place and in their current order. A press on a row that is not selected SHALL never drag: moving selects cells, as in a spreadsheet. Releasing a press without moving SHALL act as a click: a plain click opens the item, Shift-click toggles its selection, and a click on a pill opens its dropdown. Dropping with the pointer over a group's own header row SHALL put the item at the end of that group, not of the group above. Titles SHALL line up under the Title header whether or not their row has subtasks.

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

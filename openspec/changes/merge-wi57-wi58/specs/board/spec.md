## ADDED Requirements

### Requirement: Move rows in the list
Each list row SHALL have a drag grip. Dragging it in front of another row of its group SHALL place it there and switch the sort to "my order"; dragging it onto another group SHALL move it there as a drop on that board column would. A selected row SHALL carry every selected row. While dragging, the rows passed SHALL slide aside, and after the drop the rows SHALL slide to their new places.

#### Scenario: Reorder
- **WHEN** the user drags the third Planned row's grip in front of the first
- **THEN** it becomes the first Planned row and the sort reads "my order"

#### Scenario: Change group
- **WHEN** the user drags a Planned row below the last In Progress row
- **THEN** its status becomes In Progress and it is the last row of that group

### Requirement: Move rows with the keyboard
With rows selected (Shift-click, as on the board; the list has no tick boxes), the arrow keys SHALL move them as on the board, whatever has the keyboard focus, as long as the user is not typing in a field.

#### Scenario: Arrow after selecting a row
- **WHEN** the user Shift-clicks a row and presses the up arrow
- **THEN** the row moves up one place and the page does not scroll

### Requirement: Select cells in the list
Dragging across the cells of work item rows, from any cell, SHALL select the rectangle between the first and the last cell. Esc, or a click outside the table, SHALL clear it. Ctrl+C SHALL copy the rectangle as tab-separated text, one line per row. The table's text SHALL NOT be selectable as plain text.

#### Scenario: A still click is the cell's own
- **WHEN** the user presses on a status cell and releases without moving
- **THEN** the status menu opens and nothing is selected; a press that moves to another cell selects cells and opens nothing

#### Scenario: Shift-click selects the row
- **WHEN** the user Shift-clicks (or Ctrl/⌘-clicks) a cell
- **THEN** the row is selected as a card is on the board, and no cells are

#### Scenario: Copy a column of ids
- **WHEN** the user drags from the ID cell of the first Backlog row to the ID cell of the fourth and presses Ctrl+C
- **THEN** the clipboard holds the four ids, one per line, and nothing else; a row's several features are joined by commas

### Requirement: Groups and subtasks in the list
Each group header in the list SHALL have a chevron that collapses that group to its heading and opens it again. The side menu's compact switch SHALL hide every row's subtasks and show them all again; it SHALL NOT collapse groups, and there SHALL be no separate "collapse all" control.

#### Scenario: Collapse one group
- **WHEN** the user clicks the chevron of the Planned group
- **THEN** Planned collapses to its heading; the other groups stay as they were

#### Scenario: Subtasks follow the compact switch
- **WHEN** the list shows every subtask and the user turns the compact switch on in the side menu
- **THEN** the subtask rows disappear while every work item row and group stays; off again, every subtask shows

## MODIFIED Requirements

### Requirement: List table
The list view SHALL be a data grid with columns ID, Title, Status, Label, Features, Contributors, Priority, Progress and Updated, over the same rows the board shows, grouped by the current grouping with collapsible group headers showing a count and an "+ Add item" row; an empty column SHALL keep its group with a "No items" row. The user SHALL be able to sort by any column (clicking a header sorts, again reverses, a third click clears; several columns at once), filter a column from the funnel in its header (operator and value, optionally a second pair joined by and/or), resize columns, and reorder them by dragging the grip at the left of a header. A column SHALL be hidden from the × in its header and listed under Hidden in the side menu, where the eye brings it back, exactly as a board column is. The grid SHALL carry no toolbar or filter row of its own. All of this SHALL be remembered per repository.

#### Scenario: Sort by header
- **WHEN** the user clicks the Updated header twice
- **THEN** rows are sorted by date modified, first newest-first, then oldest-first, and the header shows the direction

#### Scenario: Filter
- **WHEN** the user opens the Title header's funnel, types "github" and applies
- **THEN** only rows whose title contains it remain, in their groups, and the other groups disappear until the filter is cleared

#### Scenario: Hide a column
- **WHEN** the user clicks the × in the Progress header and widens Title, then reopens the repository
- **THEN** Progress stays hidden, the side menu lists it under Hidden with an eye that brings it back, and Title keeps its width

### Requirement: Expand rows in the list
Each list row with subtasks SHALL have an expand control that shows its subtasks as indented child rows with tick boxes. The side menu's compact switch SHALL hide every row's subtasks and keep the work items and groups as they are; off again, every row shows its subtasks. There SHALL be no other control for this. Status and priority SHALL be editable directly in their cells. Cells other than the title SHALL stay on one line.

#### Scenario: Expand one row
- **WHEN** the user expands #wi123
- **THEN** its five subtasks appear under it as rows 123.1 to 123.5, and ticking one updates tasks.md

#### Scenario: Expand all
- **WHEN** the compact switch is turned on, then off
- **THEN** first every work item stays but no subtask shows, then every row with subtasks shows them

## ADDED Requirements

### Requirement: List layout
The list SHALL be laid out like the list views of task managers (Asana, Linear, ClickUp): no frame, one-line rows of about 40px, a sticky header whose controls stay hidden until hovered, a drag grip that appears on hover, compact group headers set apart by space, a faint "+ Add item" row under each group, cells in their colours, and a pointer that stays an arrow until a drag is under way.

#### Scenario: Colour
- **WHEN** a row shows its status, label and priority
- **THEN** each is a pill filled with its colour (the same as on the work item page), and a status group's name in its header is filled with the status band

#### Scenario: Resize a column
- **WHEN** the user hovers a column boundary in the header
- **THEN** a line marks the boundary and the header shows nothing else; dragging it draws the line down the whole table until release, and the column takes the new width

#### Scenario: Pointer
- **WHEN** the user hovers a header, a row's grip or a board column's header
- **THEN** the pointer stays an arrow; it turns to a grabbing hand only once a drag is under way

#### Scenario: One-line rows
- **WHEN** a title is longer than its column
- **THEN** it is cut with an ellipsis and shown whole in a tooltip; the status, label and priority are pills in their colours; features show the first by name and the rest as a count, all named in a tooltip

#### Scenario: Quiet header
- **WHEN** the user hovers the Title header
- **THEN** its sort mark, funnel and hide × appear; the other headers show only their name, except the sort mark on a sorted column

#### Scenario: Row controls on hover
- **WHEN** the user hovers a row
- **THEN** its drag grip appears and the row is tinted; other rows show neither. There are no tick boxes: Shift-click selects a row as it selects a card